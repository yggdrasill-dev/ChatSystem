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

	// 訊息匯流排：connect.deliver.{nodeId} 訂閱，交給 Adaptare.Nats 管理訂閱生命週期
	builder.AddNatsClient("message-bus");

	// 這個 process 自己的節點識別碼，啟動時產生一次
	var nodeId = new GatewayNodeId(Guid.NewGuid().ToString("N"));
	builder.Services.AddSingleton(nodeId);

	builder.Services.AddSingleton<ConnectionRegistry>();
	builder.Services.AddSingleton<ConnectionLifecycle>();

	// 使用者管理層/業務層還沒設計，先用 no-op 佔位，之後直接換掉這個註冊即可
	builder.Services.AddSingleton<IInboundMessageHandler, NoOpInboundMessageHandler>();

	builder.Services
		.AddMessageQueue()
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			.AddHandler<DeliverPacketHandler>($"connect.deliver.{nodeId.Value}"));

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
