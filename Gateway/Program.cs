using Common;
using Gateway.Models;
using Gateway.Services;
using NATS.Client.Core;

var builder = WebApplication.CreateBuilder(args);
{
	builder.AddServiceDefaults();

	// ConnectionDirectory（Redis）：連線建立/關閉時寫入、Dispatcher 讀取路由
	builder.AddRedisClient("connection-directory");
	builder.Services.AddConnectionDirectory();

	// 連線關閉時對上層發事件（房間層要靠它做斷線退房）
	builder.Services.AddConnectionEventPublisher();

	// 訊息匯流排：connect.deliver.{nodeId} / connect.terminate.{nodeId} 訂閱，
	// 交給 Adaptare.Nats 管理訂閱生命週期
	builder.AddNatsClient("message-bus");

	// 身分層（identity-store 是它自己的 Redis，不共用 connection-directory）：
	// handshake 時驗 cookie 換出 principal，並在 Supersede 時踢掉舊連線——後者要
	// IConnectionTerminator，所以 Gateway 這裡也需要它的發送端。
	builder.AddKeyedRedisClient("identity-store");
	builder.Services.AddIdentityStores("identity-store");
	builder.Services.AddConnectionTerminator();
	builder.Services.AddConnectionAuthenticator();

	// handshake 的 Origin allowlist。空的 allowlist 代表全部拒絕——這是刻意的 fail-closed：
	// 漏設定的症狀是「連不上」，而不是「任何網站都連得上」。
	builder.Services.AddSingleton(new AllowedOrigins(
		builder.Configuration.GetSection("Gateway:AllowedOrigins").Get<string[]>() ?? []));

	// 這個 process 自己的節點識別碼，啟動時產生一次
	var nodeId = new GatewayNodeId(Guid.NewGuid().ToString("N"));
	builder.Services.AddSingleton(nodeId);

	builder.Services.AddSingleton<ConnectionRegistry>();
	builder.Services.AddSingleton<ConnectionLifecycle>();

	// 上行封包交給協定層的 InboundBridge（request/reply 到 command.inbound），
	// 由 CommandRouter 負責解析與分派。Gateway 本身不解讀 payload。
	builder.Services.AddInboundBridge();

	// 這個 process 唯一一次 Adaptare message queue 註冊。多一次就會讓每個訂閱被建立兩份、
	// 每則下行訊息被投遞兩次——理由見 Common/NatsMessagingRegistration.cs。
	builder.Services
		.AddMessageQueue()
		.AddNatsGlobPatternExchange("*")
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			.AddHandler<DeliverPacketHandler>($"connect.deliver.{nodeId.Value}")
			.AddHandler<TerminatePacketHandler>($"connect.terminate.{nodeId.Value}"))
		.AddNatsGlobPatternExchange("*");

	builder.Services.AddHostedService<ConnectionHeartbeatService>();
}

var app = builder.Build();
{
	app.MapDefaultEndpoints();

	app.UseWebSockets();
	app.MapGatewayWebSocket("/ws");

	app.MapGet("/", () => "Gateway is running.");
}

app.Run();
