using Dispatcher;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ConnectionDirectory（Redis）：批次查詢 ConnectionId -> NodeId
builder.AddRedisClient("connection-directory");
builder.Services.AddConnectionDirectory();

// 訊息匯流排：訂閱 dispatch.deliver（掛 queue group），投遞到 connect.deliver.{nodeId}
builder.AddNatsClient("message-bus");

builder.Services
	.AddMessageQueue()
	.AddNatsGlobPatternExchange("*")
	.AddNatsMessageQueue(config => config
		.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
		.AddHandler<DispatchHandler>("dispatch.deliver", "dispatch.deliver"));

var host = builder.Build();
host.Run();
