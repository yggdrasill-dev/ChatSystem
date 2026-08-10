using Adaptare;
using Chat.Protos;
using CommandRouter;
using Common;
using Common.Connections;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;
using Dispatcher;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Status = Chat.Protos.RoomOperationReply.Types.Status;

namespace Integration.Tests;

// 房間層的命令流程。harness 與它涵蓋／不涵蓋什麼，見 CommandFlowHost——聊天層跟這裡共用
// 同一個（`ChatCommandFlowTests`），所以它被抽到自己的檔案裡。
public class RoomCommandFlowTests
{
	[Fact]
	public async Task Create_RepliesOkWithTheNewRoomId()
	{
		await using var host = await CommandFlowHost.StartAsync();

		var roomId = await host.CreateRoomAsync("alice", "Lobby", "hunter2");

		Assert.Equal(32, roomId.Length);
	}

	[Fact]
	public async Task Join_RepliesWrongPassword_AndNobodyIsTold()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", "hunter2");

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId, Password = "nope" });

		// 密碼錯是**業務**失敗，所以一定要有明確的下行訊息（protocol-layer.md ADR-8）——
		// 協定層對這種情況只會回 ack OK，沉默的話 client 會什麼都收不到、看起來像卡住。
		Assert.Equal(Status.WrongPassword, host.Single<RoomOperationReply>().Status);
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomJoined);
	}

	[Fact]
	public async Task Join_RepliesWithTheMemberList_AndTellsTheExistingMembers()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", "hunter2");

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId, Password = "hunter2" });
		host.Clear();

		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId, Password = "hunter2" });

		// bob 拿到完整名單，alice 拿到「bob 加入了」——這整條 fan-out（userId → connectionId
		// → dispatch.deliver → 依 node 分組 → connect.deliver.{node}）都是真的元件在跑
		var joined = host.Single<RoomJoined>();
		Assert.Equal(["alice", "bob"], joined.MemberUserIds.Order());
		Assert.Equal([host.ConnectionOf("bob")], host.TargetsOf<RoomJoined>());

		Assert.Equal("bob", host.Single<RoomMemberJoined>().UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberJoined>());
	}

	[Fact]
	public async Task Join_DoesNotAnnounceAgain_WhenTheUserIsAlreadyAMember()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		// 重連會再送一次 room.join。ADR-2 承諾「其他成員什麼都看不到」。
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });

		Assert.NotNull(host.Single<RoomJoined>());
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomMemberJoined);
	}

	[Fact]
	public async Task Join_LeavesTheCurrentRoom_AndTellsItsRemainingMembers()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var first = await host.CreateRoomAsync("alice", "First", string.Empty);
		var second = await host.CreateRoomAsync("alice", "Second", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = first });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = first });
		host.Clear();

		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = second });

		// 「一次只能在一間房」：舊房間的成員要收到 RoomMemberLeft，否則名單上會留幽靈
		var left = host.Single<RoomMemberLeft>();
		Assert.Equal(first, left.RoomId);
		Assert.Equal("bob", left.UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberLeft>());
	}

	[Fact]
	public async Task Kick_RepliesNotOwner_ForANonOwner()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);
		host.Clear();

		await host.SendAsync("bob", "room.kick", new KickMemberRequest { RoomId = roomId, TargetUserId = "alice" });

		Assert.Equal(Status.NotOwner, host.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Kick_TellsTheTargetAndTheRest()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("alice", "room.kick", new KickMemberRequest { RoomId = roomId, TargetUserId = "bob" });

		Assert.Equal([host.ConnectionOf("bob")], host.TargetsOf<RoomKicked>());
		Assert.Equal("bob", host.Single<RoomMemberLeft>().UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberLeft>());
	}

	[Fact]
	public async Task Leave_RepliesNotAMember_WhenTheUserIsSomewhereElse()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);
		host.Clear();

		await host.SendAsync("bob", "room.leave", new LeaveRoomRequest { RoomId = roomId });

		// 無條件 Remove 的話，client 傳錯 roomId 會靜默成功，還會對一間他不在的房間廣播離開
		Assert.Equal(Status.NotAMember, host.Single<RoomOperationReply>().Status);
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomMemberLeft);
	}

	[Fact]
	public async Task Leave_TellsTheRemainingMembers()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("bob", "room.leave", new LeaveRoomRequest { RoomId = roomId });

		Assert.Equal("bob", host.Single<RoomMemberLeft>().UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberLeft>());
	}

	[Fact]
	public async Task List_ExposesOnlyWhetherARoomHasAPassword_AndCountsLiveMembers()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var locked = await host.CreateRoomAsync("alice", "Locked", "hunter2");
		await host.CreateRoomAsync("alice", "Open", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = locked, Password = "hunter2" });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = locked, Password = "hunter2" });
		host.Clear();

		await host.SendAsync("carol", "room.list", new ListRoomsRequest());

		var rooms = host.Single<RoomList>().Rooms.ToDictionary(room => room.Name);

		// 只揭露有沒有密碼，不揭露密碼本身（ADR-6）
		Assert.True(rooms["Locked"].HasPassword);
		Assert.False(rooms["Open"].HasPassword);
		Assert.Equal(2, rooms["Locked"].MemberCount);
		Assert.Equal(0, rooms["Open"].MemberCount);
	}

	[Fact]
	public async Task Ban_KeepsTheMemberButBlocksRejoining_AndUnbanReversesIt()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("alice", "room.ban", new BanMemberRequest { RoomId = roomId, TargetUserId = "bob" });

		// ADR-5 的立場：封鎖不順手踢人，UI 要把兩者合成一個動作
		Assert.Equal(Status.Ok, host.Single<RoomOperationReply>().Status);
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomMemberLeft);

		host.Clear();
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		Assert.Equal(Status.Banned, host.Single<RoomOperationReply>().Status);

		host.Clear();
		await host.SendAsync(
			"alice",
			"room.ban",
			new BanMemberRequest { RoomId = roomId, TargetUserId = "bob", Unban = true });

		host.Clear();
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		Assert.NotNull(host.Single<RoomJoined>());
	}

	[Fact]
	public async Task Close_TellsTheMembers_ThenTheRoomCannotBeJoined()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("alice", "room.close", new CloseRoomRequest { RoomId = roomId });

		Assert.Equal(roomId, host.Single<RoomClosed>().RoomId);
		Assert.Equal([host.ConnectionOf("alice"), host.ConnectionOf("bob")], host.TargetsOf<RoomClosed>());

		// 關閉之後成員被清掉，房間本身也不在了
		Assert.Empty(await host.Membership.GetMembersAsync(roomId));

		host.Clear();
		await host.SendAsync("carol", "room.join", new JoinRoomRequest { RoomId = roomId });

		// ADR-10：關房＝刪房，所以晚到的人得到的是「沒有這間房」而不是「這間房關了」
		Assert.Equal(Status.RoomNotFound, host.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Update_CanClearThePassword_AndCanSetANewOne()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", "hunter2");

		await host.SendAsync(
			"alice",
			"room.update",
			new UpdateRoomRequest { RoomId = roomId, Name = "Open Lobby", ClearPassword = true });
		host.Clear();

		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		Assert.NotNull(host.Single<RoomJoined>());

		host.Clear();
		await host.SendAsync(
			"alice",
			"room.update",
			new UpdateRoomRequest { RoomId = roomId, Name = "Locked Again", Password = "s3cret" });
		host.Clear();

		await host.SendAsync("carol", "room.join", new JoinRoomRequest { RoomId = roomId, Password = "hunter2" });
		Assert.Equal(Status.WrongPassword, host.Single<RoomOperationReply>().Status);

		host.Clear();
		await host.SendAsync("carol", "room.join", new JoinRoomRequest { RoomId = roomId, Password = "s3cret" });
		Assert.NotNull(host.Single<RoomJoined>());
	}

	[Fact]
	public async Task GracePeriod_KeepsTheMember_ThenTheSweeperAnnouncesTheLeaveOnceItExpires()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		// 等同斷線事件抵達房間層（RoomDisconnectSubscriber 做的就是這一步）
		await host.Membership.MarkDisconnectedAsync("bob", host.ConnectionOf("bob"));

		// 寬限期內：還算成員，sweeper 沒事做
		Assert.Equal(2, (await host.Membership.GetMembersAsync(roomId)).Count);
		await host.SweepRoomsAsync();
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomMemberLeft);

		host.Advance(InMemoryRoomMembership.GracePeriod + TimeSpan.FromSeconds(1));

		// 正確性來自「讀取時過濾」，不是 sweeper——時間一到，名單立刻就對了（ADR-2）
		Assert.Equal(1, (await host.Membership.GetMembersAsync(roomId)).Count);

		// sweeper 只負責讓別人知道，以及真的清掉
		await host.SweepRoomsAsync();

		var left = host.Single<RoomMemberLeft>();
		Assert.Equal("bob", left.UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberLeft>());
	}

	[Fact]
	public async Task GracePeriod_IsClearedByReconnecting_SoTheSweeperNeverFires()
	{
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.Membership.MarkDisconnectedAsync("bob", host.ConnectionOf("bob"));
		host.Clear();

		// 重連（client 會再送一次 room.join）→ 標記被清掉
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Advance(InMemoryRoomMembership.GracePeriod + TimeSpan.FromSeconds(1));

		host.Clear();
		await host.SweepRoomsAsync();

		// 其他成員全程什麼都沒看到
		Assert.Empty(host.Delivered);
		Assert.Equal(2, (await host.Membership.GetMembersAsync(roomId)).Count);
	}

	[Fact]
	public async Task ProcessorAndHandler_CoexistInOneDirectQueue()
	{
		// 這個 harness 本身就是那個組合：command.inbound 是 AddProcessor，
		// dispatch.deliver 與 connect.deliver.{node} 是 AddHandler。在真的 NATS 上這個組合會讓
		// handler 完全收不到訊息（見 room-layer.md 第 9 節），Direct 上不會——所以那個限制是
		// Adaptare.Nats 專屬的，不是這個 API 形狀的問題。
		await using var host = await CommandFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });

		Assert.NotEmpty(host.Delivered);
	}
}
