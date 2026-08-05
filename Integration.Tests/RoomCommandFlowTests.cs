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
			builder.Services.AddSingleton(TimeProvider.System);

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

			builder.Services.AddSingleton<IConnectionTerminator>(new NoopConnectionTerminator());

			m_Host = builder.Build();
			await m_Host.StartAsync();

			foreach (var userId in (string[])["alice", "bob", "carol"])
			{
				await Presence.BindConnectionAsync(userId, ConnectionOf(userId));
				await connections.RegisterAsync(ConnectionOf(userId), NodeId);
			}
		}
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

	// 房間層不該終止任何連線（ADR-5），但協定層的 filter 政策需要這個依賴存在。
	private sealed class NoopConnectionTerminator : IConnectionTerminator
	{
		public ValueTask TerminateAsync(
			IReadOnlyCollection<string> connectionIds,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("房間層不該終止連線（room-layer.md ADR-5）。");
	}
}
