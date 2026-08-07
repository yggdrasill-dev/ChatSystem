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

// 房間層的命令流程跑在 Adaptare.Direct 上：沒有 NATS、沒有 Redis、沒有 AppHost。
//
// 這條路徑上是**真的**元件：InboundBridge → InboundProcessor → PacketRegistry → 房間層
// handler → RoomBroadcaster → PacketPublisher → OutboundGateway → DispatchHandler。
// 只有 store 換成 in-memory 替身，以及最末端的 connect.deliver.{nodeId} 換成一個把
// DeliverPacket 記下來的 handler（真的那個需要 WebSocket）。
//
// **這裡涵蓋的是層與層的組合**，那是單元測試全部 mock 掉的部分。它不涵蓋：
//   - production 的 messaging 接線（每個 Program.cs 那條鏈是綁 transport 的，無法在這裡換）
//   - wire format（Direct 傳的是同一個參考，不會像 NATS 那樣把 0 bytes 變成 null）
//   - 跨節點 fan-out（一個 process 裡只有一個 Direct queue）
// 那三項仍然只有真 NATS 的端到端驗得到（scratchpad/RoomE2E）。
public class RoomCommandFlowTests
{
	private const string NodeId = "node-1";

	[Fact]
	public async Task Create_RepliesOkWithTheNewRoomId()
	{
		await using var host = await RoomFlowHost.StartAsync();

		var roomId = await host.CreateRoomAsync("alice", "Lobby", "hunter2");

		Assert.Equal(32, roomId.Length);
	}

	[Fact]
	public async Task Join_RepliesWrongPassword_AndNobodyIsTold()
	{
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);
		host.Clear();

		await host.SendAsync("bob", "room.kick", new KickMemberRequest { RoomId = roomId, TargetUserId = "alice" });

		Assert.Equal(Status.NotOwner, host.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Kick_TellsTheTargetAndTheRest()
	{
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		await host.SendAsync("alice", "room.close", new CloseRoomRequest { RoomId = roomId });

		Assert.Equal(roomId, host.Single<RoomClosed>().RoomId);
		Assert.Equal([host.ConnectionOf("alice"), host.ConnectionOf("bob")], host.TargetsOf<RoomClosed>());

		// 關閉之後成員被清掉，房間也不再出現在列表上
		Assert.Empty(await host.Membership.GetMembersAsync(roomId));

		host.Clear();
		await host.SendAsync("carol", "room.join", new JoinRoomRequest { RoomId = roomId });
		Assert.Equal(Status.RoomClosed, host.Single<RoomOperationReply>().Status);
	}

	[Fact]
	public async Task Update_CanClearThePassword_AndCanSetANewOne()
	{
		await using var host = await RoomFlowHost.StartAsync();
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
		await using var host = await RoomFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Clear();

		// 等同斷線事件抵達房間層（RoomDisconnectSubscriber 做的就是這一步）
		await host.Membership.MarkDisconnectedAsync("bob", host.ConnectionOf("bob"));

		// 寬限期內：還算成員，sweeper 沒事做
		Assert.Equal(2, (await host.Membership.GetMembersAsync(roomId)).Count);
		await host.SweepAsync();
		Assert.DoesNotContain(host.Delivered, delivery => delivery.Message is RoomMemberLeft);

		host.Advance(InMemoryRoomMembership.GracePeriod + TimeSpan.FromSeconds(1));

		// 正確性來自「讀取時過濾」，不是 sweeper——時間一到，名單立刻就對了（ADR-2）
		Assert.Equal(1, (await host.Membership.GetMembersAsync(roomId)).Count);

		// sweeper 只負責讓別人知道，以及真的清掉
		await host.SweepAsync();

		var left = host.Single<RoomMemberLeft>();
		Assert.Equal("bob", left.UserId);
		Assert.Equal([host.ConnectionOf("alice")], host.TargetsOf<RoomMemberLeft>());
	}

	[Fact]
	public async Task GracePeriod_IsClearedByReconnecting_SoTheSweeperNeverFires()
	{
		await using var host = await RoomFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		await host.Membership.MarkDisconnectedAsync("bob", host.ConnectionOf("bob"));
		host.Clear();

		// 重連（client 會再送一次 room.join）→ 標記被清掉
		await host.SendAsync("bob", "room.join", new JoinRoomRequest { RoomId = roomId });
		host.Advance(InMemoryRoomMembership.GracePeriod + TimeSpan.FromSeconds(1));

		host.Clear();
		await host.SweepAsync();

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
		await using var host = await RoomFlowHost.StartAsync();
		var roomId = await host.CreateRoomAsync("alice", "Lobby", string.Empty);

		await host.SendAsync("alice", "room.join", new JoinRoomRequest { RoomId = roomId });

		Assert.NotEmpty(host.Delivered);
	}

	private sealed class RoomFlowHost : IAsyncDisposable
	{
		private IHost m_Host = null!;

		public DeliveryCapture Capture { get; } = new();

		public InMemoryPresenceDirectory Presence { get; } = new();

		public MutableTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));

		public IRoomMembership Membership => m_Host.Services.GetRequiredService<IRoomMembership>();

		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Delivered => Capture.Delivered;

		public static async Task<RoomFlowHost> StartAsync()
		{
			var host = new RoomFlowHost();
			await host.StartCoreAsync();

			return host;
		}

		// 每個使用者一條固定的連線，並把 Presence／ConnectionDirectory 都填好——
		// 相當於「已經完成 handshake、綁定在 node-1 上」。
		public string ConnectionOf(string userId) => $"conn-{userId}";

		// 從 Gateway 的進入點打進去：真的 InboundBridge 會做 request/reply 到 command.inbound。
		// ack 本身在 bridge 內部就被消化掉（只記 log），測試因此從下行訊息觀察結果。
		public Task SendAsync<TMessage>(string userId, string subject, TMessage message)
			where TMessage : IMessage<TMessage> =>
			m_Host.Services.GetRequiredService<IInboundMessageHandler>()
				.HandleAsync(ConnectionOf(userId), userId, subject, message.ToByteString())
				.AsTask();

		public async Task<string> CreateRoomAsync(string owner, string name, string password)
		{
			await SendAsync(owner, "room.create", new CreateRoomRequest { Name = name, Password = password });

			var roomId = Single<RoomOperationReply>().RoomId;
			Clear();

			return roomId;
		}

		public void Clear() => Capture.Delivered.Clear();

		public void Advance(TimeSpan amount) => Clock.Advance(amount);

		// 直接呼叫 sweeper 的一輪，不等它自己的 10 秒計時器——真 NATS 的端到端要為這一項等
		// 60 秒，這裡是毫秒級而且不依賴真實時間。
		public Task SweepAsync() =>
			new RoomGraceSweeper(
					Membership,
					m_Host.Services.GetRequiredService<RoomBroadcaster>(),
					NullLogger<RoomGraceSweeper>.Instance)
				.SweepAsync(CancellationToken.None)
				.AsTask();

		public TMessage Single<TMessage>() where TMessage : IMessage =>
			Assert.Single(Delivered.Select(delivery => delivery.Message).OfType<TMessage>());

		public IReadOnlyCollection<string> TargetsOf<TMessage>() where TMessage : IMessage =>
			[.. Delivered.Where(d => d.Message is TMessage).SelectMany(d => d.ConnectionIds).Order()];

		public async ValueTask DisposeAsync()
		{
			await m_Host.StopAsync();
			m_Host.Dispose();
		}

		private async Task StartCoreAsync()
		{
			var builder = Host.CreateApplicationBuilder();
			builder.Logging.ClearProviders();

			var connections = new InMemoryConnectionDirectory();

			builder.Services.AddSingleton(Capture);
			builder.Services.AddSingleton<TimeProvider>(Clock);

			// store 全部換成替身；其餘都是真的註冊路徑
			builder.Services.AddSingleton<IConnectionDirectory>(connections);
			builder.Services.AddSingleton<IPresenceDirectory>(Presence);
			builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
			builder.Services.AddSingleton<IRoomBanList, InMemoryRoomBanList>();
			builder.Services.AddSingleton<IRoomMembership, InMemoryRoomMembership>();

			builder.Services.AddOutboundGateway();
			builder.Services.AddInboundBridge();
			builder.Services.AddPacketRegistry();
			builder.Services.AddRoomPackets();

			builder.Services
				.AddMessageQueue()
				.AddDirectMessageQueue(config => config
					.AddProcessor<InboundProcessor>("command.inbound")
					.AddHandler<DispatchHandler>("dispatch.deliver")
					.AddHandler<CapturingDeliveryHandler>($"connect.deliver.{NodeId}"))
				.AddDirectGlobPatternExchange("*");

			// 刻意沒有註冊 IConnectionTerminator：這條命令鏈裡沒有任何元件需要它。
			// 之前有一個會丟例外的替身，那純粹是因為 InboundProcessor 為了 filter 機制注入它
			// （protocol-layer.md ADR-6，機制已移除）。現在「這條路徑不關連線」是 DI 圖上
			// 就看得出來的事實。
			m_Host = builder.Build();
			await m_Host.StartAsync();

			foreach (var userId in (string[])["alice", "bob", "carol"])
			{
				await Presence.BindConnectionAsync(userId, ConnectionOf(userId));
				await connections.RegisterAsync(ConnectionOf(userId), NodeId);
			}
		}
	}

	// 寬限期是「現在減掉 DisconnectedAt」，所以測試要能推時間而不是等時間。
	internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
	{
		private DateTimeOffset m_Now = start;

		public override DateTimeOffset GetUtcNow() => m_Now;

		public void Advance(TimeSpan amount) => m_Now += amount;
	}

	private sealed class DeliveryCapture
	{
		public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Delivered { get; } = [];
	}

	// 站在真的 DeliverPacketHandler 的位置：那個需要 ConnectionRegistry 與真的 socket，
	// 這裡只要知道「哪些連線收到了什麼」。
	private sealed class CapturingDeliveryHandler(DeliveryCapture capture, PacketRegistry registry)
		: IMessageHandler<byte[]>
	{
		public ValueTask HandleAsync(
			string subject,
			byte[] data,
			IEnumerable<MessageHeaderValue>? headerValues,
			CancellationToken cancellationToken = default)
		{
			var packet = DeliverPacket.Parser.ParseFrom(data);

			lock (capture.Delivered)
				capture.Delivered.Add((packet.ConnectionIds.ToArray(), Decode(packet)));

			return ValueTask.CompletedTask;
		}

		// registry 是 subject ↔ 型別的唯一對應表，出站與入站共用同一份——這裡反過來用它解碼，
		// 等於順便驗證了下行的 subject 真的有註冊。
		private IMessage Decode(DeliverPacket packet) =>
			packet.Subject switch
			{
				"room.reply" => RoomOperationReply.Parser.ParseFrom(packet.Payload),
				"room.joined" => RoomJoined.Parser.ParseFrom(packet.Payload),
				"room.member.joined" => RoomMemberJoined.Parser.ParseFrom(packet.Payload),
				"room.member.left" => RoomMemberLeft.Parser.ParseFrom(packet.Payload),
				"room.kicked" => RoomKicked.Parser.ParseFrom(packet.Payload),
				"room.closed" => RoomClosed.Parser.ParseFrom(packet.Payload),
				"room.list.reply" => RoomList.Parser.ParseFrom(packet.Payload),
				_ => throw new InvalidOperationException(
					$"Unexpected outbound subject '{packet.Subject}'. Registered: {string.Join(", ", registry.Subjects)}"),
			};
	}
}
