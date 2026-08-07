using Chat.Protos;
using MessagePacket = Chat.Protos.ChatMessage;

namespace E2E.Tests;

// 聊天層在真的 AppHost 上跑一遍。**這裡不重跑 Integration.Tests 已經釘住的每一條行為**
// （那邊 0.6 秒跑完 11 條），只驗那三件 Adaptare.Direct 結構上蓋不到的事：
//   1. production 的 messaging 接線——AddChatPackets() 真的被 CommandRouter/Program.cs 呼叫了，
//      subject 字面值沒打錯，registry 在啟動時解析得出來
//   2. wire format——訊息真的走完 protobuf 序列化 → NATS → WebSocket frame 回到 client
//   3. 跨節點 fan-out——兩個成員落在不同 Gateway 複本時，訊息仍然到得了
//
// 階段 A 的前提：CommandRouter 只有一個複本（AppHost 沒有對它 WithReplicas），所以
// InMemoryChatMessageStore 對整個叢集是一致的。**它一旦開複本，這裡的歷史查詢就會開始隨機
// 失敗**——那不是測試的問題，是階段 A 的儲存還沒換成 PostgreSQL（chat-layer.md ADR-4）。
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
