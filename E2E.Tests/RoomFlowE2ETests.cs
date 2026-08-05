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
		// 先照一張快照，之後才分得出哪些連線是這個測試自己開的。
		var before = await fixture.SnapshotConnectionsAsync();

		await using var alice = await ConnectAsync("xnode-a");
		await using var bob = await ConnectAsync("xnode-b");

		var mine = (await fixture.SnapshotConnectionsAsync())
			.Where(entry => !before.ContainsKey(entry.Key))
			.ToDictionary(entry => entry.Key, entry => entry.Value);

		Assert.Equal(2, mine.Count);

		// Aspire 對開了 replica 的資源會在 endpoint 前面放一個 proxy 輪流分派，所以兩條連線
		// 通常會落在不同的 Gateway。**這個斷言不是在驗 Aspire**，是在確保下面那個廣播真的跨了
		// 節點——如果兩條落在同一個節點，這個測試就沒有驗到它宣稱要驗的東西，寧可紅掉。
		Assert.Equal(2, mine.Values.Distinct().Count());

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

		await alice.SendAsync(
			"room.ban",
			new BanMemberRequest { RoomId = roomId, TargetUserId = bob.UserId, Unban = true });

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

		Assert.Equal(RoomOperationReply.Types.Status.RoomClosed, rejected.Status);
	}

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
