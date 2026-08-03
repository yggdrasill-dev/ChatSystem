using Dispatcher;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);
{
	builder.AddServiceDefaults();

	// ConnectionDirectory（Redis）：批次查詢 ConnectionId -> NodeId
	builder.AddRedisClient("connection-directory");
	builder.Services.AddConnectionDirectory();

	// 訊息匯流排：訂閱 dispatch.deliver / dispatch.terminate（掛 queue group），
	// 分別投遞到 connect.deliver.{nodeId} / connect.terminate.{nodeId}
	builder.AddNatsClient("message-bus");

	builder.Services
		.AddMessageQueue()
		.AddNatsGlobPatternExchange("*")
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			.AddHandler<DispatchHandler>("dispatch.deliver", "dispatch.deliver")
			.AddHandler<TerminateHandler>("dispatch.terminate", "dispatch.terminate"));
}

var host = builder.Build();
host.Run();
