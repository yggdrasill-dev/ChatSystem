using CommandRouter;
using Common.Protocol;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);
{
	builder.AddServiceDefaults();

	// 下行要用 IOutboundGateway（回訊息）與 IConnectionTerminator（狀態違反時關連線），
	// 兩者都經 Dispatcher，所以這裡需要 ConnectionDirectory 的 Redis 與訊息匯流排。
	builder.AddRedisClient("connection-directory");
	builder.AddNatsClient("message-bus");
	builder.Services.AddConnectionDirectory();
	builder.Services.AddOutboundGateway();
	builder.Services.AddConnectionTerminator();

	// 協定層本身：subject ↔ 型別的對應表與出口
	builder.Services.AddPacketRegistry();

	// 房間層：房間/封鎖名單/成員名單住在自己的 Redis（room-store）。
	// fan-out 要把 userId 換成 connectionId，所以也需要身分層的 IPresenceDirectory
	// ——它住在 identity-store，跟 room-store 是兩個不同的 Redis 資源。
	builder.AddKeyedRedisClient("room-store");
	builder.AddKeyedRedisClient("identity-store");
	builder.Services.AddRoomStore("room-store");
	builder.Services.AddIdentityStores("identity-store");
	builder.Services.AddRoomPackets();

	builder.Services
		.AddMessageQueue()
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			.AddProcessor<InboundProcessor>("command.inbound", "command.inbound"));
}

var host = builder.Build();
{
	// 在這裡解析 PacketRegistry 有兩個目的：把對應表印進 log 方便排查，以及讓重複註冊的
	// fail fast 發生在啟動時而不是第一則訊息進來時（ADR-4 失去編譯期檢查的補償）。
	var registry = host.Services.GetRequiredService<PacketRegistry>();

	host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
		"Registered {SubjectCount} subject(s): {Subjects}",
		registry.Subjects.Count,
		registry.Subjects.Count == 0 ? "(none)" : string.Join(", ", registry.Subjects));
}

host.Run();
