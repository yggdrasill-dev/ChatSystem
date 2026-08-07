using CommandRouter;
using Common.Protocol;
using Common.Rooms;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);
{
	builder.AddServiceDefaults();

	// 下行要用 IOutboundGateway（回訊息），它經 Dispatcher，所以這裡需要 ConnectionDirectory
	// 的 Redis 與訊息匯流排。
	//
	// 刻意沒有 AddConnectionTerminator()：協定層唯一的終止路徑是 IInboundFilter 的
	// Terminate，那個機制已經移除（protocol-layer.md ADR-6），所以這個 process 不再有任何
	// 關掉使用者連線的能力。終止連線目前只剩身分層的 Supersede 在用。
	builder.AddRedisClient("connection-directory");
	builder.AddNatsClient("message-bus");
	builder.Services.AddConnectionDirectory();
	builder.Services.AddOutboundGateway();

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
	builder.Services.AddRoomMembershipMaintenance();

	// 這個 process 唯一一次 Adaptare message queue 註冊（見 Common/NatsMessagingRegistration.cs）。
	builder.Services
		.AddMessageQueue()
		.AddNatsGlobPatternExchange("*")
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			// processor 與 handler 在同一條鏈裡共存沒有問題（實測過兩者都會被呼叫）。用哪一種
			// 取決於語意：command.inbound 要回 ack 所以是 processor，斷線事件是
			// fire-and-forget 所以是 handler。
			.AddProcessor<InboundProcessor>("command.inbound", "command.inbound")
			.AddHandler<RoomDisconnectHandler>(RoomDisconnectHandler.Subject, RoomDisconnectHandler.QueueGroup));
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
