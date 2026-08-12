using Adaptare;
using Chat.Protos;
using CommandRouter;
using Common;
using Common.Chat;
using Common.Connections;
using Common.Identity;
using Common.Protocol;
using Common.Rooms;
using Common.Tests.Chat;
using Common.Tests.Rooms;
using Dispatcher;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Integration.Tests;

// 房間層與聊天層共用的 harness：整條命令流程跑在 Adaptare.Direct 上，沒有 NATS、沒有 Redis、
// 沒有 AppHost。
//
// 這條路徑上是**真的**元件：InboundBridge → InboundProcessor → PacketRegistry → 業務層
// handler → RoomBroadcaster → PacketPublisher → OutboundGateway → 真的 DispatchHandler。
// 只有 store 換成 in-memory 替身，以及最末端的 connect.deliver.{nodeId} 換成一個把
// DeliverPacket 記下來的 handler（真的那個需要 WebSocket）。
//
// **這裡涵蓋的是層與層的組合**，那是單元測試全部 mock 掉的部分。它不涵蓋：
//   - production 的 messaging 接線（每個 Program.cs 那條鏈是綁 transport 的，無法在這裡換）
//   - wire format（Direct 傳的是同一個參考，不會像 NATS 那樣把 0 bytes 變成 null）
//   - 跨節點 fan-out（一個 process 裡只有一個 Direct queue）
// 那三項仍然只有真 NATS 的端到端驗得到（E2E.Tests）。
internal sealed class CommandFlowHost : IAsyncDisposable
{
	public const string NodeId = "node-1";

	private IHost m_Host = null!;

	public DeliveryCapture Capture { get; } = new();

	public InMemoryPresenceDirectory Presence { get; } = new();

	public MutableTimeProvider Clock { get; } = new(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000));

	public IRoomMembership Membership => m_Host.Services.GetRequiredService<IRoomMembership>();

	public IChatMessageStore Messages => m_Host.Services.GetRequiredService<IChatMessageStore>();

	// 聊天層的顯示名稱快照來自這裡（ADR-3）。登入不在這條命令鏈上，所以測試自己餵。
	public IUserProfileStore Profiles => m_Host.Services.GetRequiredService<IUserProfileStore>();

	public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Delivered => Capture.Delivered;

	public static async Task<CommandFlowHost> StartAsync()
	{
		var host = new CommandFlowHost();
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

	// 建房 + 讓每個人加入 + 清掉那些加入通知，讓聊天層的測試從一個乾淨的狀態開始。
	public async Task<string> CreateRoomWithMembersAsync(params string[] userIds)
	{
		var roomId = await CreateRoomAsync(userIds[0], "Lobby", string.Empty);

		foreach (var userId in userIds)
			await SendAsync(userId, "room.join", new JoinRoomRequest { RoomId = roomId });

		Clear();

		return roomId;
	}

	public void Clear() => Capture.Delivered.Clear();

	public void Advance(TimeSpan amount) => Clock.Advance(amount);

	// 直接呼叫 sweeper 的一輪，不等它自己的計時器——真 NATS 的端到端要為寬限期那一項等
	// 60 秒，這裡是毫秒級而且不依賴真實時間。
	public Task SweepRoomsAsync() =>
		new RoomGraceSweeper(
				Membership,
				m_Host.Services.GetRequiredService<RoomBroadcaster>(),
				NullLogger<RoomGraceSweeper>.Instance)
			.SweepAsync(CancellationToken.None)
			.AsTask();

	public async Task<int> SweepMessagesAsync(ChatRetention retention) =>
		await new ChatRetentionSweeper(
				Messages,
				retention,
				Clock,
				NullLogger<ChatRetentionSweeper>.Instance)
			.SweepAsync(CancellationToken.None);

	public TMessage Single<TMessage>() where TMessage : IMessage =>
		Assert.Single(Delivered.Select(delivery => delivery.Message).OfType<TMessage>());

	public IReadOnlyCollection<TMessage> All<TMessage>() where TMessage : IMessage =>
		[.. Delivered.Select(delivery => delivery.Message).OfType<TMessage>()];

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

		// store 全部換成替身；其餘都是真的註冊路徑。
		//
		// **這些是「取代」不是「覆寫」**：正式路徑上這三個 store 由 Common.Storage 的 AddChatDb()
		// 註冊，而這個 host 從來不呼叫它——所以沒有「後註冊蓋掉前註冊」這種需要讀兩處才看得懂的
		// 依賴。（EF Core 的組件仍然會透過 CommandRouter 的專案參考進到輸出目錄，這個專案要
		// InboundProcessor。但它一行都不會執行。）
		builder.Services.AddSingleton<IConnectionDirectory>(connections);
		builder.Services.AddSingleton<IPresenceDirectory>(Presence);
		builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
		builder.Services.AddSingleton<IRoomBanList, InMemoryRoomBanList>();
		builder.Services.AddSingleton<IChatMessageStore, InMemoryChatMessageStore>();
		builder.Services.AddSingleton<IRoomMembership, InMemoryRoomMembership>();
		builder.Services.AddSingleton<IUserProfileStore, InMemoryUserProfileStore>();
		// 限流也是替身了：正式路徑上它由 AddChatRateLimiting("chat-ratelimit") 註冊 Redis 版，
		// 而這個 host 跟其他 store 一樣**不呼叫那個方法**——所以這裡仍然是取代、不是覆寫。
		builder.Services.AddSingleton<IChatRateLimiter>(
			sp => new InMemoryChatRateLimiter(sp.GetRequiredService<TimeProvider>(), ChatRateLimit.Default));

		builder.Services.AddOutboundGateway();
		builder.Services.AddInboundBridge();
		builder.Services.AddPacketRegistry();
		builder.Services.AddRoomPackets();

		// 聊天層剩下的部分（設定與發號）是真的註冊路徑；訊息 store 與限流的替身跟房間層的兩個
		// 一起註冊在上面。**B1 之前這裡連替身都不需要**——AddChatStore() 那時註冊的本來就是
		// in-memory 的實作，所以整個聊天層是完整的正式註冊，那是個巧合，訊息搬進 Postgres
		// 就結束了一半，限流換 Redis 之後另一半也結束了。
		builder.Services.AddChatCore();
		builder.Services.AddChatPackets();

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

internal sealed class DeliveryCapture
{
	public List<(IReadOnlyCollection<string> ConnectionIds, IMessage Message)> Delivered { get; } = [];
}

// 站在真的 DeliverPacketHandler 的位置：那個需要 ConnectionRegistry 與真的 socket，
// 這裡只要知道「哪些連線收到了什麼」。
internal sealed class CapturingDeliveryHandler(DeliveryCapture capture, PacketRegistry registry)
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
			// 全名：領域的 Common.Chat.ChatMessage 同名（見 Common/Chat/Handlers/ChatPacket.cs）。
			"chat.message" => Chat.Protos.ChatMessage.Parser.ParseFrom(packet.Payload),
			"chat.history.reply" => ChatHistory.Parser.ParseFrom(packet.Payload),
			"chat.reply" => ChatOperationReply.Parser.ParseFrom(packet.Payload),
			_ => throw new InvalidOperationException(
				$"Unexpected outbound subject '{packet.Subject}'. Registered: {string.Join(", ", registry.Subjects)}"),
		};
}

// 身分層目前只有 Redis 實作，而登入不在這條命令鏈上——聊天層要的顯示名稱快照因此得由
// 測試自己餵（ADR-3）。
internal sealed class InMemoryUserProfileStore : IUserProfileStore
{
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> m_DisplayNames = new();

	public ValueTask SaveAsync(
		string userId,
		string? displayName,
		string? pictureUrl,
		CancellationToken cancellationToken = default)
	{
		m_DisplayNames[userId] = displayName ?? string.Empty;

		return ValueTask.CompletedTask;
	}

	public ValueTask<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_DisplayNames.TryGetValue(userId, out var displayName) ? displayName : null);
}
