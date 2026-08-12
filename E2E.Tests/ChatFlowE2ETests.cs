using Chat.Protos;
using MessagePacket = Chat.Protos.ChatMessage;

namespace E2E.Tests;

// 聊天層在真的 AppHost 上跑一遍。**這裡不重跑 Integration.Tests 已經釘住的每一條行為**
// （那邊 0.6 秒跑完 11 條），只驗那幾件 Adaptare.Direct 結構上蓋不到的事：
//   1. production 的 messaging 接線——AddChatPackets() 真的被 CommandRouter/Program.cs 呼叫了，
//      subject 字面值沒打錯，registry 在啟動時解析得出來
//   2. wire format——訊息真的走完 protobuf 序列化 → NATS → WebSocket frame 回到 client
//   3. 跨節點 fan-out——兩個成員落在不同 Gateway 複本時，訊息仍然到得了
//   4. **限流真的接上了**——Integration.Tests 那條路徑上的限流是 in-memory 替身
//      （`CommandFlowHost` 自己註冊的），所以 `AddChatRateLimiting()` 有沒有被 production 呼叫、
//      Redis 的 keyed client 有沒有接對，只有這裡看得見
//
// ~~階段 A 的前提：CommandRouter 只有一個複本，所以 InMemoryChatMessageStore 對整個叢集是一致
// 的。它一旦開複本，這裡的歷史查詢就會開始隨機失敗。~~ **訊息在 Postgres（B1）、限流在 Redis
// （B3）之後那個前提消失了，AppHost 現在真的開了兩個複本**——所以下面每一條歷史查詢都可能由
// 另一個複本回答，而那是刻意的：這些測試因此順便驗了「哪個複本回答都一樣」。
[Collection(AppHostCollection.Name)]
public class ChatFlowE2ETests(AppHostFixture fixture)
{
	[E2EFact]
	public async Task Send_ReachesEveryMember_AndShowsUpInHistory()
	{
		await using var alice = await ConnectAsync("chat-a");
		await using var bob = await ConnectAsync("chat-b");

		var roomId = await JoinNewRoomAsync(alice, bob);

		await alice.SendAsync("chat.send", new SendChatMessageRequest { Body = "hello from alice" });

		// 兩邊都收到，包含送出者自己——那正是他拿到 order_key 的方式。
		var toAlice = await alice.ExpectAsync("chat.message", MessagePacket.Parser);
		var toBob = await bob.ExpectAsync("chat.message", MessagePacket.Parser);

		Assert.Equal(roomId, toAlice.RoomId);
		Assert.Equal("hello from alice", toAlice.Body);
		Assert.Equal(alice.UserId, toAlice.SenderUserId);
		Assert.Equal(toAlice.OrderKey, toBob.OrderKey);

		// order_key 低於 JS 的 2^53，webClient 可以直接當 number 用（ADR-2 不選 snowflake
		// 的理由之一）。在真的 wire 上驗一次，因為 int64 的序列化是這條斷言真正的對象。
		Assert.True(toAlice.OrderKey is > 0 and < 1L << 53);

		// 先存後廣播（ADR-1）：既然廣播到了，歷史裡就一定有。
		bob.Clear();
		await bob.SendAsync("chat.history", new ChatHistoryRequest());

		var history = await bob.ExpectAsync("chat.history.reply", ChatHistory.Parser);

		Assert.Equal(roomId, history.RoomId);
		Assert.Equal(
			[("hello from alice", toAlice.OrderKey)],
			history.Messages.Select(message => (message.Body, message.OrderKey)));
	}

	[E2EFact]
	public async Task Send_RepliesNotInRoom_WhenTheSenderHasNotJoined()
	{
		await using var alice = await ConnectAsync("chat-nowhere");

		await alice.SendAsync("chat.send", new SendChatMessageRequest { Body = "anyone?" });

		// ChatOperationReply 的 OK 是 0，所以這條同時證明了「非 OK 的回覆在真的 wire 上回得來」。
		// 成功路徑不回這個型別（廣播本身就是確認），所以 0 bytes 那個陷阱在這裡碰不到——
		// 理由寫在 chat.proto 的 enum 上。
		var reply = await alice.ExpectAsync("chat.reply", ChatOperationReply.Parser);

		Assert.Equal(ChatOperationReply.Types.Status.NotInRoom, reply.Status);
	}

	// 限流（ADR-7）在**正式註冊**上真的生效。Integration.Tests 蓋不到這件事——那條路徑上的限流器
	// 是 `CommandFlowHost` 自己註冊的 in-memory 替身，所以「`AddChatRateLimiting("chat-ratelimit")`
	// 有沒有被呼叫、那個 keyed Redis client 有沒有接對」在那邊是隱形的。
	//
	// **它證明的不是「計數跨複本共用」。** 那一半由契約測試守（兩個實例、同一顆 Redis，
	// `TwoInstances_ShareTheCount_LikeTwoReplicasDo`）；這裡送出去的命令會落在哪個複本無法指定，
	// 所以這條測試在單一複本上也會綠。兩者合起來才是完整的：接線在這裡、語意在那裡。
	[E2EFact]
	public async Task Send_IsRateLimited_WhenOneUserFloodsTheRoom()
	{
		await using var alice = await ConnectAsync("chat-flood");

		await JoinNewRoomAsync(alice);

		// 上限是每秒 10 則（`ChatRateLimit.Default`）。一次送 40 則**不等回覆**——Gateway 的
		// receive loop 逐則處理，所以只要吞吐量高於 10 則/秒就一定會撞到上限，而實測是毫秒級。
		// 刻意不精算「前 10 則過、第 11 則被擋」：這個 burst 可能跨過秒的邊界（固定視窗），
		// 那會讓確切的通過數不穩定，而**確切的數字不是這條測試的斷言對象**。
		for (var i = 0; i < 40; i++)
			await alice.SendAsync("chat.send", new SendChatMessageRequest { Body = $"flood {i}" });

		// 成功的送出不回 chat.reply（廣播本身就是確認），所以這條路徑上收到 chat.reply 就是被擋了。
		var reply = await alice.ExpectAsync("chat.reply", ChatOperationReply.Parser);

		Assert.Equal(ChatOperationReply.Types.Status.RateLimited, reply.Status);

		// 而且被擋掉的是「多的那些」，不是全部——前面確實有訊息進得去。
		await alice.ExpectAsync("chat.message", MessagePacket.Parser);

		// 視窗滾過去之後額度回來（key 帶著 unixSecond + EXPIRE 2）。等 1.5 秒而不是 1 秒：
		// burst 的最後一則落在哪一秒不確定，多給半秒就不必猜。
		await Task.Delay(TimeSpan.FromSeconds(1.5));
		alice.Clear();

		await alice.SendAsync("chat.send", new SendChatMessageRequest { Body = "after the window" });

		var accepted = await alice.ExpectAsync("chat.message", MessagePacket.Parser);

		Assert.Equal("after the window", accepted.Body);
	}

	[E2EFact]
	public async Task Send_ReachesAMemberOnADifferentGatewayNode()
	{
		// 跟 RoomFlowE2ETests 那條同樣的理由：fixture 的就緒檢查只證明**至少一個** replica 會
		// 回應，所以要開到落在不同節點為止，而不是斷言「剛好不同」。詳細脈絡見該檔案。
		var (alice, bob) = await ConnectToDifferentNodesAsync("chat-xnode");

		await using (alice)
		await using (bob)
		{
			await JoinNewRoomAsync(alice, bob);

			await bob.SendAsync("chat.send", new SendChatMessageRequest { Body = "across the cluster" });

			// alice 在另一個節點上，所以這則訊息一定經過 Dispatcher 的分組與跨節點投遞。
			// 聊天層自己沒有 fan-out 程式碼——它重用房間層的 RoomBroadcaster（6.8），
			// 所以這條驗的是「重用真的接對了」。
			var received = await alice.ExpectAsync("chat.message", MessagePacket.Parser);

			Assert.Equal("across the cluster", received.Body);
			Assert.Equal(bob.UserId, received.SenderUserId);
		}
	}

	private static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

	private Task<ChatClient> ConnectAsync(string prefix) => ChatClient.ConnectAsync(fixture, UniqueId(prefix));

	// 建房、兩個人都加入、並把加入通知清掉，讓聊天層的斷言從乾淨的狀態開始。
	private static async Task<string> JoinNewRoomAsync(ChatClient owner, params ChatClient[] others)
	{
		await owner.SendAsync("room.create", new CreateRoomRequest { Name = UniqueId("room") });

		var created = await owner.ExpectAsync("room.reply", RoomOperationReply.Parser);

		Assert.Equal(RoomOperationReply.Types.Status.Ok, created.Status);

		foreach (var client in others.Prepend(owner))
		{
			await client.SendAsync("room.join", new JoinRoomRequest { RoomId = created.RoomId });
			await client.ExpectAsync("room.joined", RoomJoined.Parser);
		}

		foreach (var client in others.Prepend(owner))
			client.Clear();

		return created.RoomId;
	}

	private async Task<(ChatClient First, ChatClient Second)> ConnectToDifferentNodesAsync(string prefix)
	{
		var before = await fixture.SnapshotConnectionsAsync();
		var first = await ConnectAsync($"{prefix}-a");
		var firstNode = await NodeOfNewConnectionAsync(before);

		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

		for (var attempt = 0; DateTime.UtcNow < deadline; attempt++)
		{
			var beforeCandidate = await fixture.SnapshotConnectionsAsync();
			var candidate = await ConnectAsync($"{prefix}-b{attempt}");

			if (await NodeOfNewConnectionAsync(beforeCandidate) != firstNode)
				return (first, candidate);

			await candidate.DisposeAsync();
			await Task.Delay(500);
		}

		await first.DisposeAsync();

		throw new InvalidOperationException(
			$"每一條連線都落在 {firstNode}——實際上只有一個 Gateway replica 在服務，這個測試沒有"
			+ "驗到它宣稱要驗的跨節點投遞，所以紅掉是對的。");
	}

	// client 不知道自己的 connectionId（那是伺服器端的概念），所以用連線前後的 Conn:* 差集
	// 反推。註冊是 handshake 之後才寫進 Redis 的，所以要等它出現。
	private async Task<string> NodeOfNewConnectionAsync(IReadOnlyDictionary<string, string> before)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

		while (DateTime.UtcNow < deadline)
		{
			var appeared = (await fixture.SnapshotConnectionsAsync())
				.Where(entry => !before.ContainsKey(entry.Key))
				.ToList();

			if (appeared.Count == 1)
				return appeared[0].Value;

			await Task.Delay(100);
		}

		throw new TimeoutException("新連線沒有在 10 秒內出現在 ConnectionDirectory 裡。");
	}
}
