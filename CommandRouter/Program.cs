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

	// 各層在這裡註冊自己的命令與 filter，例如身分層會加上：
	//   builder.Services.AddPacketHandler<BindRequest, IdentityBindHandler>("identity.bind");
	//   builder.Services.AddOutboundPacket<BindReply>("identity.bind.reply");
	//   builder.Services.AddInboundFilter<IdentityBoundFilter>();
	// 身分層尚未實作，所以目前 registry 是空的——任何 client 命令都會被回 UNKNOWN_SUBJECT。

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
