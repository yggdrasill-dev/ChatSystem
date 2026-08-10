using Chat.Protos;
using Common.Rooms;

namespace E2E.Tests;

// 跑真的 AppHost：4 個服務（Gateway ×2 replica、Dispatcher、CommandRouter、WebBff）
// + 3 個容器（NATS、connection-directory、room-store、identity-store）。
//
// 每個測試用自己的 userId 與房名（帶隨機後綴）——identity-store 與 room-store 都掛了
// data volume 且開持久化，狀態會跨執行留下來，所以任何「總數」型的斷言都不可靠，
// 只能斷言「這一輪自己造出來的東西」。
[Collection(AppHostCollection.Name)]
public class RoomFlowE2ETests(AppHostFixture fixture)
{
	private static readonly TimeSpan _GracePeriod = RedisRoomMembership.GracePeriod;
	private static readonly TimeSpan _SweeperInterval = RoomGraceSweeper.Interval;

	// 等第二個 Gateway replica 開始服務的期限。它不是「等 replica 啟動」的時間預算——
	// AppHost 早就啟動完了——而是「proxy 開始把連線分到第二個 replica」的期限。
	private static readonly TimeSpan _CrossNodeTimeout = TimeSpan.FromSeconds(30);

	[E2EFact]
	public async Task Login_ThenHandshake_EstablishesAWorkingConnection()
	{
		await using var alice = await ConnectAsync("login");

		// 走得完 handshake 就代表 cookie 驗證、Origin allowlist、principal 轉交都接對了。
		Assert.Equal(System.Net.WebSockets.WebSocketState.Open, alice.State);
	}

	[E2EFact]
	public async Task RoomCommands_FlowThroughTheRealMessagingChain()
	{
		await using var alice = await ConnectAsync("create");

		var (roomId, name) = await CreateRoomAsync(alice);

		// 這一條同時驗到 command.inbound 的 request/reply（ack 沒回來連線會在 10 秒被關掉）、
		// PacketRegistry 的雙向對應，以及 dispatch.deliver -> connect.deliver.{nodeId} 的下行。
		await alice.SendAsync("room.list", new ListRoomsRequest());
		var list = await alice.ExpectAsync("room.list.reply", RoomList.Parser);
		var summary = Assert.Single(list.Rooms, room => room.RoomId == roomId);

		Assert.Equal(name, summary.Name);
		Assert.False(summary.HasPassword);

		await alice.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		var joined = await alice.ExpectAsync("room.joined", RoomJoined.Parser);

		Assert.Equal(roomId, joined.RoomId);
		Assert.Contains(alice.UserId, joined.MemberUserIds);
	}

	[E2EFact]
	public async Task Broadcast_ReachesAMemberOnADifferentGatewayNode()
	{
		var aliceBefore = await fixture.SnapshotConnectionsAsync();
		await using var alice = await ConnectAsync("xnode-a");
		var aliceNode = await NodeOfNewConnectionAsync(aliceBefore);

		// Aspire 對開了 replica 的資源會在 endpoint 前面放一個 proxy 輪流分派，但
		// **「至少一個 replica 會回應」不等於「兩個都在服務」**——`AppHostFixture` 的就緒檢查
		// 只打得到那個 proxy，分不出後面站著幾個（它刻意不猜 gateway-0/gateway-1 這種衍生
		// 名字，理由見該檔案）。第二個 replica 還沒開始聽的時候，開幾條連線都會落在同一個節點。
		//
		// 本節原本直接 `Assert.Equal(2, mine.Values.Distinct().Count())`，於是這個測試會隨啟動
		// 時序紅掉，而紅掉的原因不是產品壞了、是它自己沒等到第二個 replica。改成**開到落在
		// 不同節點為止**：下面那個廣播仍然一定跨節點，**這個測試該驗的東西一項都沒少**——
		// 差別只在環境時序不再被當成失敗。
		var bob = await ConnectToAnotherNodeAsync(aliceNode)
			?? throw new InvalidOperationException(
				$"在 {_CrossNodeTimeout} 內開的每一條連線都落在 {aliceNode}——實際上只有一個 " +
				"Gateway replica 在服務，這個測試沒有驗到它宣稱要驗的跨節點投遞，所以紅掉是對的。");

		await using (bob)
		{
			var (roomId, _) = await CreateRoomAsync(alice);

			await alice.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
			await alice.ExpectAsync("room.joined", RoomJoined.Parser);

			await bob.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
			await bob.ExpectAsync("room.joined", RoomJoined.Parser);

			// alice 在另一個節點上，所以這則廣播一定經過 Dispatcher 的分組與跨節點投遞。
			var memberJoined = await alice.ExpectAsync("room.member.joined", RoomMemberJoined.Parser);

			Assert.Equal(roomId, memberJoined.RoomId);
			Assert.Equal(bob.UserId, memberJoined.UserId);
		}
	}

	[E2EFact]
	public async Task PasswordRoom_RejectsTheWrongPassword_AndAcceptsTheRightOne()
	{
		await using var alice = await ConnectAsync("pw");

		var (roomId, _) = await CreateRoomAsync(alice, password: "hunter2");

		await alice.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId, Password = "nope" });
		var rejected = await alice.ExpectAsync("room.reply", RoomOperationReply.Parser);

		Assert.Equal(RoomOperationReply.Types.Status.WrongPassword, rejected.Status);

		await alice.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId, Password = "hunter2" });
		var joined = await alice.ExpectAsync("room.joined", RoomJoined.Parser);

		Assert.Equal(roomId, joined.RoomId);
	}

	[E2EFact]
	public async Task Kick_RemovesTheMember_ButLeavesTheConnectionOpen()
	{
		await using var alice = await ConnectAsync("kick-owner");
		await using var bob = await ConnectAsync("kick-target");

		var (roomId, _) = await CreateRoomAsync(alice);
		await JoinAsync(alice, roomId);
		await JoinAsync(bob, roomId);

		await alice.SendAsync("room.kick", new KickMemberRequest { RoomId = roomId, TargetUserId = bob.UserId });

		var kicked = await bob.ExpectAsync("room.kicked", RoomKicked.Parser);
		Assert.Equal(roomId, kicked.RoomId);

		// ADR-5：踢出房間不等於關連線。被踢的人要能留在連線上去加入別的房間。
		Assert.Equal(System.Net.WebSockets.WebSocketState.Open, bob.State);

		await bob.SendAsync("room.list", new ListRoomsRequest());
		await bob.ExpectAsync("room.list.reply", RoomList.Parser);
	}

	[E2EFact]
	public async Task Ban_BlocksRejoin_AndUnbanReverses()
	{
		await using var alice = await ConnectAsync("ban-owner");
		await using var bob = await ConnectAsync("ban-target");

		var (roomId, _) = await CreateRoomAsync(alice);
		await JoinAsync(alice, roomId);
		await JoinAsync(bob, roomId);

		await alice.SendAsync("room.ban", new BanMemberRequest { RoomId = roomId, TargetUserId = bob.UserId });
		await alice.SendAsync("room.kick", new KickMemberRequest { RoomId = roomId, TargetUserId = bob.UserId });
		await bob.ExpectAsync("room.kicked", RoomKicked.Parser);

		bob.Clear();
		await bob.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		var banned = await bob.ExpectAsync("room.reply", RoomOperationReply.Parser);

		Assert.Equal(RoomOperationReply.Types.Status.Banned, banned.Status);

		// **必須等 unban 真的完成才能讓 bob 送 join。** 兩個命令來自不同連線，而
		// protocol-layer.md ADR-2 只保證「單一連線同時只有一則訊息 in-flight」——跨連線沒有
		// 任何順序保證。不等的話 join 會讀到還沒被清掉的封鎖名單，回 BANNED 而不是 room.joined。
		//
		// 上面 ban + kick 那段沒有這個問題，是因為兩者都從 alice 送出（同一條連線＝有序），
		// 而且測試接著等 bob 的 room.kicked，那等於間接證明 ban 已經生效。
		alice.Clear();
		await alice.SendAsync(
			"room.ban",
			new BanMemberRequest { RoomId = roomId, TargetUserId = bob.UserId, Unban = true });

		var unbanned = await alice.ExpectAsync("room.reply", RoomOperationReply.Parser);
		Assert.Equal(RoomOperationReply.Types.Status.Ok, unbanned.Status);

		bob.Clear();
		await bob.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		var joined = await bob.ExpectAsync("room.joined", RoomJoined.Parser);

		Assert.Equal(roomId, joined.RoomId);
	}

	[E2EFact]
	public async Task Close_BroadcastsRoomClosed_AndLaterJoinsAreRejected()
	{
		await using var alice = await ConnectAsync("close-owner");
		await using var bob = await ConnectAsync("close-member");

		var (roomId, _) = await CreateRoomAsync(alice);
		await JoinAsync(alice, roomId);
		await JoinAsync(bob, roomId);

		await alice.SendAsync("room.close", new CloseRoomRequest { RoomId = roomId });

		var closed = await bob.ExpectAsync("room.closed", RoomClosed.Parser);
		Assert.Equal(roomId, closed.RoomId);

		bob.Clear();
		await bob.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		var rejected = await bob.ExpectAsync("room.reply", RoomOperationReply.Parser);

		// ADR-10：關房＝刪房，所以晚到的人得到的是 ROOM_NOT_FOUND
		Assert.Equal(RoomOperationReply.Types.Status.RoomNotFound, rejected.Status);
	}

	// **這一條的存在理由是 ADR-10 帶進來的那段 Lua。** 改設定現在走一段腳本，而單元測試 stub 掉
	// ScriptEvaluateAsync、整合測試走 in-memory 替身——兩邊都不會真的執行它。語法錯或回傳型別不對
	// 只有真 Redis 分辨得出來，所以這裡驗的是「它跑得起來而且真的寫進去了」。守門本身驗不到，
	// 那條在下面的 RoomStore_RefusesToResurrectADeletedRoom。
	[E2EFact]
	public async Task Update_RunsTheScriptAgainstRealRedis()
	{
		await using var alice = await ConnectAsync("update-owner");

		var (roomId, _) = await CreateRoomAsync(alice, "s3cret");
		var renamed = UniqueId("renamed");

		await alice.SendAsync(
			"room.update",
			new UpdateRoomRequest { RoomId = roomId, Name = renamed, ClearPassword = true });

		Assert.Equal(
			RoomOperationReply.Types.Status.Ok,
			(await alice.ExpectAsync("room.reply", RoomOperationReply.Parser)).Status);

		// 腳本回 1 只代表它跑完了，HSET 有沒有真的寫進去要從 room.list 讀回來看。
		await alice.SendAsync("room.list", new ListRoomsRequest());
		var summary = Assert.Single(
			(await alice.ExpectAsync("room.list.reply", RoomList.Parser)).Rooms,
			room => room.RoomId == roomId);

		Assert.Equal(renamed, summary.Name);
		Assert.False(summary.HasPassword);

	}

	// 下面兩條**不經過 client 命令**，直接對真 room-store 呼叫 IRoomStore。兩條性質都是 ADR-10
	// 帶進來的，而且都在「client 走不到、in-memory 替身湊不出來」的夾縫裡——見
	// AppHostFixture.ConnectRoomStoreAsync 的註解。
	[E2EFact]
	public async Task RoomStore_RefusesToResurrectADeletedRoom()
	{
		await using var probe = await fixture.ConnectRoomStoreAsync();

		var roomId = UniqueId("zombie");
		Assert.True(await probe.Store.TryCreateAsync(NewRoom(roomId)));
		Assert.True(await probe.Store.TryDeleteAsync(roomId));

		// **這正是 RoomUpdateHandler 走不到的那條路徑**：它自己會先 GetAsync，房間不在就直接回
		// ROOM_NOT_FOUND，所以循序的 client 命令永遠不會讓 HSET 撞上一個不存在的 key。真正會走到
		// 這裡的是 update 與 close 交錯的那個窗口。少了腳本裡那行 EXISTS，HSET 會把房間建回來一半
		// ——只有 name 與 password_hash、沒有房主。
		Assert.False(await probe.Store.TryUpdateSettingsAsync(roomId, "Zombie", null));
		Assert.Null(await probe.Store.GetAsync(roomId));
	}

	[E2EFact]
	public async Task RoomStore_DeleteTakesTheBanListWithIt()
	{
		await using var probe = await fixture.ConnectRoomStoreAsync();

		var roomId = UniqueId("cascade");
		Assert.True(await probe.Store.TryCreateAsync(NewRoom(roomId)));
		await probe.Bans.BanAsync(roomId, "banned-user");

		Assert.True(await probe.Store.TryDeleteAsync(roomId));

		// Postgres 版靠 ON DELETE CASCADE，Redis 版只能在 TryDeleteAsync 裡自己多刪一個 key
		// （ADR-10）。留著的話那個集合永遠不會再被讀到——roomId 是 Guid、不會重用——但那正是
		// ADR-10 想消滅的孤兒形狀，所以這裡把它釘住。
		Assert.False(await probe.Bans.IsBannedAsync(roomId, "banned-user"));
	}

	private static Room NewRoom(string roomId) =>
		new(roomId, "Lobby", null, "probe-owner", DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));

	[E2EFact]
	public async Task GracePeriod_ReconnectWithinTheWindow_IsInvisibleToOtherMembers()
	{
		await using var alice = await ConnectAsync("grace-watcher");

		var bobId = UniqueId("grace-flapper");
		var bobSession = await ChatClient.LoginAsync(fixture, bobId);
		var bob = await ChatClient.ConnectAsync(fixture, bobId, bobSession);

		var (roomId, _) = await CreateRoomAsync(alice);
		await JoinAsync(alice, roomId);
		await JoinAsync(bob, roomId);
		await alice.ExpectAsync("room.member.joined", RoomMemberJoined.Parser);

		alice.Clear();

		// 硬中止：這條路徑才會讓伺服器端的 RequestAborted 觸發，也才是真實世界的斷線。
		bob.Abort();
		await bob.DisposeAsync();

		// 同一個 session 重連並重新加入。ADR-2：寬限期內成員資格還在，重連對其他人完全無感。
		await using var bobAgain = await ChatClient.ConnectAsync(fixture, bobId, bobSession);
		await bobAgain.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		await bobAgain.ExpectAsync("room.joined", RoomJoined.Parser);

		await alice.ExpectNothingAsync("room.member.left", TimeSpan.FromSeconds(3));
		await alice.ExpectNothingAsync("room.member.joined", TimeSpan.FromSeconds(1));
	}

	[E2EFact]
	public async Task GracePeriod_AfterExpiry_TheSweeperBroadcastsThatTheMemberLeft()
	{
		// **這是整組裡最重要的一條。** 它驗的是連線層 -> 房間層那條事件通道真的活著：
		// Gateway 發 events.connection.disconnected -> CommandRouter 的 RoomDisconnectHandler
		// 標記進寬限期 -> 到期後 RoomGraceSweeper 廣播離開。
		//
		// 這條路徑曾經整段是死的（斷線清理把已取消的 CancellationToken 傳給 publish），而且
		// 沒有任何 log、單元測試全綠——所以它必須在真的 NATS 上被驗一次，不能只靠替身。
		await using var alice = await ConnectAsync("sweep-watcher");
		var bob = await ConnectAsync("sweep-leaver");

		var (roomId, _) = await CreateRoomAsync(alice);
		await JoinAsync(alice, roomId);
		await JoinAsync(bob, roomId);
		await alice.ExpectAsync("room.member.joined", RoomMemberJoined.Parser);

		alice.Clear();
		var bobId = bob.UserId;

		bob.Abort();
		await bob.DisposeAsync();

		// 寬限期內不該有任何動靜。
		await alice.ExpectNothingAsync("room.member.left", TimeSpan.FromSeconds(5));

		// 寬限期 30 秒 + sweeper 最多再等一個 10 秒的間隔，留一點餘裕。
		var left = await alice.ExpectAsync(
			"room.member.left",
			RoomMemberLeft.Parser,
			_GracePeriod + _SweeperInterval + TimeSpan.FromSeconds(15));

		Assert.Equal(roomId, left.RoomId);
		Assert.Equal(bobId, left.UserId);
	}

	[E2EFact]
	public async Task Supersede_TerminatesTheEarlierConnection()
	{
		var userId = UniqueId("supersede");
		var session = await ChatClient.LoginAsync(fixture, userId);

		await using var first = await ChatClient.ConnectAsync(fixture, userId, session);
		await using var second = await ChatClient.ConnectAsync(fixture, userId, session);

		// 身分層 ADR-4：後開的連線把先開的踢掉。這條路徑會經過 IConnectionTerminator ->
		// Dispatcher -> connect.terminate.{nodeId}，所以它同時是「上行以外的下行通道」的驗證。
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);

		while (DateTime.UtcNow < deadline && first.State == System.Net.WebSockets.WebSocketState.Open)
			await Task.Delay(100);

		Assert.NotEqual(System.Net.WebSockets.WebSocketState.Open, first.State);
		Assert.Equal(System.Net.WebSockets.WebSocketState.Open, second.State);
	}

	private static string UniqueId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

	private Task<ChatClient> ConnectAsync(string prefix) => ChatClient.ConnectAsync(fixture, UniqueId(prefix));

	// 一直開連線直到某一條落在 `otherThan` 以外的 Gateway 節點上；期限內試不到就回 null。
	//
	// 每次嘗試都用新的 userId（`UniqueId`），所以不會觸發 Supersede 把前一條踢掉；沒中的那些
	// 立刻 dispose，Gateway 會把它們從 ConnectionDirectory 移掉。**代價是幾條丟棄的連線**，
	// 換掉的是一個會隨啟動時序紅掉的斷言。
	private async Task<ChatClient?> ConnectToAnotherNodeAsync(string otherThan)
	{
		var deadline = DateTime.UtcNow + _CrossNodeTimeout;

		for (var attempt = 0; DateTime.UtcNow < deadline; attempt++)
		{
			var before = await fixture.SnapshotConnectionsAsync();
			var candidate = await ConnectAsync($"xnode-b{attempt}");

			if (await NodeOfNewConnectionAsync(before) != otherThan)
				return candidate;

			await candidate.DisposeAsync();
			await Task.Delay(500);
		}

		return null;
	}

	// client 自己不知道它的 connectionId——那是伺服器端的概念，不會回給 client——所以只能用
	// 「連線前後的 `Conn:*` 差集」反推它落在哪個節點。
	//
	// 註冊是 handshake 完成之後才寫進 Redis 的，`ConnectAsync` 回來的那一刻不保證看得到，
	// 所以要等它出現而不是只照一張快照——這正是原本那個版本沒處理、只是剛好沒踩到的時序。
	private async Task<string> NodeOfNewConnectionAsync(IReadOnlyDictionary<string, string> before)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);

		while (DateTime.UtcNow < deadline)
		{
			var appeared = (await fixture.SnapshotConnectionsAsync())
				.Where(entry => !before.ContainsKey(entry.Key))
				.ToList();

			// 同一個 collection 循序執行，所以「剛好多出一條」是穩定的：上一輪丟棄的連線就算
			// 還沒被清掉，它也在 before 裡面，不會被算成新出現的。
			if (appeared.Count == 1)
				return appeared[0].Value;

			await Task.Delay(100);
		}

		throw new TimeoutException("新連線沒有在 10 秒內出現在 ConnectionDirectory 裡。");
	}

	private static async Task<(string RoomId, string Name)> CreateRoomAsync(ChatClient owner, string password = "")
	{
		var name = UniqueId("room");

		await owner.SendAsync("room.create", new CreateRoomRequest { Name = name, Password = password });
		var reply = await owner.ExpectAsync("room.reply", RoomOperationReply.Parser);

		Assert.Equal(RoomOperationReply.Types.Status.Ok, reply.Status);
		Assert.NotEmpty(reply.RoomId);

		return (reply.RoomId, name);
	}

	private static async Task JoinAsync(ChatClient client, string roomId)
	{
		await client.SendAsync("room.join", new JoinRoomRequest { RoomId = roomId });
		await client.ExpectAsync("room.joined", RoomJoined.Parser);
	}
}
