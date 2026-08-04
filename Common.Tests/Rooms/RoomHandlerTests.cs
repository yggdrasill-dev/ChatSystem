using Chat.Protos;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;
using Common.Rooms.Handlers;
using Google.Protobuf;
using NSubstitute;
using Status = Chat.Protos.RoomOperationReply.Types.Status;

namespace Common.Tests.Rooms;

public class RoomHandlerTests
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
	private static readonly CommandContext _Alice = new("conn-alice", "alice");

	// ---- create ----

	[Fact]
	public async Task Create_StoresTheRoomOwnedByTheCaller_AndRepliesWithTheNewRoomId()
	{
		var harness = new Harness();
		harness.Store.TryCreateAsync(Arg.Any<Room>(), Arg.Any<CancellationToken>()).Returns(true);

		await new RoomCreateHandler(harness.Store, harness.Broadcaster, harness.Time)
			.HandleAsync(_Alice, new CreateRoomRequest { Name = "Lobby", Password = "s3cret" });

		var stored = harness.Store.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IRoomStore.TryCreateAsync))
			.GetArguments()[0] as Room;

		Assert.NotNull(stored);
		Assert.Equal("alice", stored.OwnerUserId);
		Assert.Equal(_Now, stored.CreatedAt);
		Assert.False(stored.IsClosed);

		// 密碼不能以可還原的形式存（ADR-6）
		Assert.NotNull(stored.PasswordHash);
		Assert.DoesNotContain("s3cret", stored.PasswordHash);

		var reply = harness.Single<RoomOperationReply>();
		Assert.Equal(Status.Ok, reply.Status);
		Assert.Equal(stored.RoomId, reply.RoomId);
	}

	[Fact]
	public async Task Create_LeavesPasswordHashNull_ForAPublicRoom()
	{
		var harness = new Harness();
		harness.Store.TryCreateAsync(Arg.Any<Room>(), Arg.Any<CancellationToken>()).Returns(true);

		await new RoomCreateHandler(harness.Store, harness.Broadcaster, harness.Time)
			.HandleAsync(_Alice, new CreateRoomRequest { Name = "Open", Password = string.Empty });

		var stored = harness.Store.ReceivedCalls()
			.Single(call => call.GetMethodInfo().Name == nameof(IRoomStore.TryCreateAsync))
			.GetArguments()[0] as Room;

		Assert.Null(stored!.PasswordHash);
	}

	// ---- join ----

	[Fact]
	public async Task Join_RepliesRoomNotFound_AndDoesNotTouchMembership()
	{
		var harness = new Harness();
		harness.Store.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((Room?)null);

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "missing" });

		Assert.Equal(Status.RoomNotFound, harness.Single<RoomOperationReply>().Status);
		await harness.Membership.DidNotReceive().JoinAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Join_RepliesRoomClosed_ForAClosedRoom()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(isClosed: true));

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-1" });

		Assert.Equal(Status.RoomClosed, harness.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Join_RepliesBanned_BeforeCheckingThePassword()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(passwordHash: RoomPasswordFor("right")));
		harness.Bans.IsBannedAsync("room-1", "alice", Arg.Any<CancellationToken>()).Returns(true);

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-1", Password = "wrong" });

		// 被封鎖的人不該從「密碼錯」這個回應推斷出密碼是對的
		Assert.Equal(Status.Banned, harness.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Join_RepliesWrongPassword_AndDoesNotJoin()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(passwordHash: RoomPasswordFor("right")));

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-1", Password = "wrong" });

		Assert.Equal(Status.WrongPassword, harness.Single<RoomOperationReply>().Status);
		await harness.Membership.DidNotReceive().JoinAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Join_AcceptsTheCorrectPassword_RepliesWithTheMemberList_AndTellsTheOthers()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(passwordHash: RoomPasswordFor("right")));
		harness.StubMembers("room-1", Member("bob"));

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-1", Password = "right" });

		await harness.Membership.Received(1).JoinAsync("room-1", "alice", "conn-alice", Arg.Any<CancellationToken>());

		var joined = harness.Single<RoomJoined>();
		Assert.Equal(["bob", "alice"], joined.MemberUserIds);
		Assert.Equal(["conn-alice"], harness.TargetsOf<RoomJoined>());

		// 加入的人自己不會收到 RoomMemberJoined，其他成員才會
		Assert.Equal("alice", harness.Single<RoomMemberJoined>().UserId);
		Assert.Equal(["conn-bob"], harness.TargetsOf<RoomMemberJoined>());
	}

	[Fact]
	public async Task Join_DoesNotBroadcastJoined_WhenTheUserWasAlreadyAMember()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());
		harness.StubMembers("room-1", Member("alice", disconnectedAt: _Now - TimeSpan.FromSeconds(5)), Member("bob"));

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-1" });

		// 寬限期內重連：ADR-2 承諾「其他成員什麼都看不到」，所以不能有 joined 廣播
		Assert.Empty(harness.Sent.Where(sent => sent.Message is RoomMemberJoined));
		Assert.NotNull(harness.Single<RoomJoined>());
	}

	[Fact]
	public async Task Join_LeavesTheCurrentRoom_AndTellsItsRemainingMembers()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(roomId: "room-2"));
		harness.Membership.GetCurrentRoomAsync("alice", Arg.Any<CancellationToken>()).Returns("room-1");
		harness.StubMembers("room-1", Member("bob"));
		harness.StubMembers("room-2");

		await harness.JoinHandler().HandleAsync(_Alice, new JoinRoomRequest { RoomId = "room-2" });

		await harness.Membership.Received(1).RemoveAsync("room-1", "alice", Arg.Any<CancellationToken>());

		// 少了這個廣播，舊房間的 client 名單上會留一個永遠不會消失的幽靈
		var left = harness.Single<RoomMemberLeft>();
		Assert.Equal("room-1", left.RoomId);
		Assert.Equal("alice", left.UserId);
		Assert.Equal(["conn-bob"], harness.TargetsOf<RoomMemberLeft>());
	}

	// ---- leave ----

	[Fact]
	public async Task Leave_RepliesNotAMember_WhenTheUserIsSomewhereElse()
	{
		var harness = new Harness();
		harness.Membership.GetCurrentRoomAsync("alice", Arg.Any<CancellationToken>()).Returns("room-2");

		await new RoomLeaveHandler(harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new LeaveRoomRequest { RoomId = "room-1" });

		// 無條件 Remove 的話，client 傳錯 roomId 會靜默成功，還會對一間他不在的房間廣播離開
		Assert.Equal(Status.NotAMember, harness.Single<RoomOperationReply>().Status);
		await harness.Membership.DidNotReceive().RemoveAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Leave_RemovesTheMember_AndTellsTheRest()
	{
		var harness = new Harness();
		harness.Membership.GetCurrentRoomAsync("alice", Arg.Any<CancellationToken>()).Returns("room-1");
		harness.StubMembers("room-1", Member("bob"));

		await new RoomLeaveHandler(harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new LeaveRoomRequest { RoomId = "room-1" });

		await harness.Membership.Received(1).RemoveAsync("room-1", "alice", Arg.Any<CancellationToken>());
		Assert.Equal(Status.Ok, harness.Single<RoomOperationReply>().Status);
		Assert.Equal("alice", harness.Single<RoomMemberLeft>().UserId);
		Assert.Equal(["conn-bob"], harness.TargetsOf<RoomMemberLeft>());
	}

	// ---- kick / ban ----

	[Fact]
	public async Task Kick_RepliesNotOwner_AndRemovesNobody()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(ownerUserId: "bob"));

		await new RoomKickHandler(harness.Store, harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new KickMemberRequest { RoomId = "room-1", TargetUserId = "carol" });

		Assert.Equal(Status.NotOwner, harness.Single<RoomOperationReply>().Status);
		await harness.Membership.DidNotReceive().RemoveAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Kick_TellsTheTargetAndTheRest_WithoutClosingAnyConnection()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());
		harness.StubMembers("room-1", Member("bob"));

		await new RoomKickHandler(harness.Store, harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new KickMemberRequest { RoomId = "room-1", TargetUserId = "carol" });

		await harness.Membership.Received(1).RemoveAsync("room-1", "carol", Arg.Any<CancellationToken>());
		Assert.Equal(["conn-carol"], harness.TargetsOf<RoomKicked>());
		Assert.Equal("carol", harness.Single<RoomMemberLeft>().UserId);

		// ADR-5：踢出房間不等於關閉連線。handler 連 IConnectionTerminator 都沒有注入，
		// 所以這件事是結構上不可能發生，而不是靠測試盯著。
		Assert.DoesNotContain(
			typeof(Common.Connections.IConnectionTerminator),
			typeof(RoomKickHandler).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
	}

	[Fact]
	public async Task Ban_UpdatesTheBanList_ButDoesNotRemoveTheMember()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());

		await new RoomBanHandler(harness.Store, harness.Bans, harness.Broadcaster)
			.HandleAsync(_Alice, new BanMemberRequest { RoomId = "room-1", TargetUserId = "carol" });

		await harness.Bans.Received(1).BanAsync("room-1", "carol", Arg.Any<CancellationToken>());

		// ADR-5 的立場是「踢出＋封鎖由 UI 合成一個動作」，所以封鎖本身不動成員名單
		await harness.Membership.DidNotReceive().RemoveAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
		Assert.Equal(Status.Ok, harness.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Ban_WithUnbanFlag_RemovesFromTheBanList()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());

		await new RoomBanHandler(harness.Store, harness.Bans, harness.Broadcaster)
			.HandleAsync(_Alice, new BanMemberRequest { RoomId = "room-1", TargetUserId = "carol", Unban = true });

		await harness.Bans.Received(1).UnbanAsync("room-1", "carol", Arg.Any<CancellationToken>());
		await harness.Bans.DidNotReceive().BanAsync(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	// ---- close ----

	[Fact]
	public async Task Close_TellsTheMembersThenClearsThem()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());
		harness.StubMembers("room-1", Member("alice"), Member("bob"));
		harness.Store.TryCloseAsync("room-1", Arg.Any<CancellationToken>()).Returns(true);

		await new RoomCloseHandler(harness.Store, harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new CloseRoomRequest { RoomId = "room-1" });

		Assert.Equal(Status.Ok, harness.Single<RoomOperationReply>().Status);
		Assert.Equal(["conn-alice", "conn-bob"], harness.TargetsOf<RoomClosed>());

		// 名單要在關閉之前讀、清理要在廣播之後做，否則不知道該通知誰
		await harness.Membership.Received(1).RemoveAsync("room-1", "alice", Arg.Any<CancellationToken>());
		await harness.Membership.Received(1).RemoveAsync("room-1", "bob", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Close_RepliesRoomClosed_AndBroadcastsNothing_WhenItWasAlreadyClosed()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom());
		harness.StubMembers("room-1", Member("bob"));
		harness.Store.TryCloseAsync("room-1", Arg.Any<CancellationToken>()).Returns(false);

		await new RoomCloseHandler(harness.Store, harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new CloseRoomRequest { RoomId = "room-1" });

		Assert.Equal(Status.RoomClosed, harness.Single<RoomOperationReply>().Status);
		Assert.Empty(harness.Sent.Where(sent => sent.Message is RoomClosed));
	}

	// ---- update ----

	[Fact]
	public async Task Update_HashesANewPassword()
	{
		var harness = new Harness();
		harness.StubRoom(NewRoom(passwordHash: RoomPasswordFor("old")));
		harness.Store
			.TryUpdateSettingsAsync("room-1", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(true);

		await new RoomUpdateHandler(harness.Store, harness.Broadcaster)
			.HandleAsync(_Alice, new UpdateRoomRequest { RoomId = "room-1", Name = "Renamed", Password = "new" });

		var hash = harness.UpdatedPasswordHash();
		Assert.NotNull(hash);
		Assert.DoesNotContain("new", hash);
		Assert.True(RoomPasswordVerify("new", hash));
	}

	[Fact]
	public async Task Update_ClearsThePassword_OnlyWhenAskedExplicitly()
	{
		var harness = new Harness();
		var existing = RoomPasswordFor("old");
		harness.StubRoom(NewRoom(passwordHash: existing));
		harness.Store
			.TryUpdateSettingsAsync("room-1", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(true);

		await new RoomUpdateHandler(harness.Store, harness.Broadcaster)
			.HandleAsync(_Alice, new UpdateRoomRequest { RoomId = "room-1", Name = "n", ClearPassword = true });

		Assert.Null(harness.UpdatedPasswordHash());
	}

	[Fact]
	public async Task Update_KeepsTheExistingPassword_WhenNoneIsGiven()
	{
		var harness = new Harness();
		var existing = RoomPasswordFor("old");
		harness.StubRoom(NewRoom(passwordHash: existing));
		harness.Store
			.TryUpdateSettingsAsync("room-1", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
			.Returns(true);

		await new RoomUpdateHandler(harness.Store, harness.Broadcaster)
			.HandleAsync(_Alice, new UpdateRoomRequest { RoomId = "room-1", Name = "Renamed" });

		// 「不動密碼」跟「改成公開房」是兩種不同的意圖，只用空字串表達不了
		Assert.Equal(existing, harness.UpdatedPasswordHash());
	}

	// ---- list ----

	[Fact]
	public async Task List_ExposesOnlyWhetherARoomHasAPassword_AndCountsLiveMembers()
	{
		var harness = new Harness();
		harness.Store.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([
			NewRoom(roomId: "open", passwordHash: null),
			NewRoom(roomId: "locked", passwordHash: RoomPasswordFor("x")),
		]);
		harness.StubMembers("open", Member("bob"), Member("carol"));
		harness.StubMembers("locked");

		await new RoomListHandler(harness.Store, harness.Membership, harness.Broadcaster)
			.HandleAsync(_Alice, new ListRoomsRequest());

		var list = harness.Single<RoomList>();

		Assert.Equal([false, true], list.Rooms.Select(room => room.HasPassword));
		Assert.Equal([2, 0], list.Rooms.Select(room => room.MemberCount));
		Assert.DoesNotContain(list.Rooms.Select(room => room.ToString()), text => text.Contains("password_hash"));
	}

	// ---- helpers ----

	private static Room NewRoom(
		string roomId = "room-1",
		string? passwordHash = null,
		string ownerUserId = "alice",
		bool isClosed = false) =>
		new(roomId, "Lobby", passwordHash, ownerUserId, _Now, isClosed);

	private static RoomMember Member(string userId, DateTimeOffset? disconnectedAt = null) =>
		new(userId, _Now, $"conn-{userId}", disconnectedAt);

	private static string RoomPasswordFor(string password) => RoomPassword.Hash(password);

	private static bool RoomPasswordVerify(string password, string? hash) => RoomPassword.Verify(password, hash);

	private sealed class Harness
	{
		public Harness()
		{
			Presence
				.ResolveConnectionsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
				.Returns(call => (IReadOnlyCollection<string>)
					[.. call.Arg<IReadOnlyCollection<string>>().Select(userId => $"conn-{userId}")]);

			Broadcaster = new RoomBroadcaster(Presence, Publisher);
		}

		public IRoomStore Store { get; } = Substitute.For<IRoomStore>();

		public IRoomBanList Bans { get; } = Substitute.For<IRoomBanList>();

		public IRoomMembership Membership { get; } = Substitute.For<IRoomMembership>();

		public IPresenceDirectory Presence { get; } = Substitute.For<IPresenceDirectory>();

		public RecordingPublisher Publisher { get; } = new();

		public RoomBroadcaster Broadcaster { get; }

		public TimeProvider Time { get; } = new FixedTimeProvider(_Now);

		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Sent => Publisher.Sent;

		public RoomJoinHandler JoinHandler() => new(Store, Bans, Membership, Broadcaster);

		public void StubRoom(Room room) =>
			Store.GetAsync(room.RoomId, Arg.Any<CancellationToken>()).Returns(room);

		public void StubMembers(string roomId, params RoomMember[] members) =>
			Membership.GetMembersAsync(roomId, Arg.Any<CancellationToken>()).Returns(members);

		public TMessage Single<TMessage>() where TMessage : IMessage =>
			Assert.Single(Sent.Select(sent => sent.Message).OfType<TMessage>());

		public IReadOnlyCollection<string> TargetsOf<TMessage>() where TMessage : IMessage =>
			[.. Sent.Where(sent => sent.Message is TMessage).SelectMany(sent => sent.ConnectionIds).Order()];

		public string? UpdatedPasswordHash() =>
			(string?)Store.ReceivedCalls()
				.Single(call => call.GetMethodInfo().Name == nameof(IRoomStore.TryUpdateSettingsAsync))
				.GetArguments()[2];
	}

	private sealed class RecordingPublisher : IPacketPublisher
	{
		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Sent { get; } = [];

		public ValueTask PublishAsync<TMessage>(
			IReadOnlyCollection<string> connectionIds,
			TMessage message,
			CancellationToken cancellationToken = default)
			where TMessage : IMessage<TMessage>
		{
			Sent.Add((connectionIds, message));

			return ValueTask.CompletedTask;
		}
	}

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
