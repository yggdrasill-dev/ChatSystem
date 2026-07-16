using Dispatcher;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ConnectionDirectory（Redis）：批次查詢 ConnectionId -> NodeId
builder.AddRedisClient("connection-directory");

// 訊息匯流排：訂閱 dispatch.deliver，投遞到 connect.deliver.{nodeId}
builder.AddNatsClient("message-bus");

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
