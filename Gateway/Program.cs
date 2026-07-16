var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ConnectionDirectory（Redis）：連線建立/關閉時寫入、Dispatcher 讀取路由
builder.AddRedisClient("connection-directory");

// 訊息匯流排：inbound 封包發布、connect.deliver.{nodeId} 訂閱
builder.AddNatsClient("message-bus");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Gateway is running.");

app.Run();
