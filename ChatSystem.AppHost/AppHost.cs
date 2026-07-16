var builder = DistributedApplication.CreateBuilder(args);

// ConnectionDirectory：連線層自己擁有的 ConnectionId -> NodeId 對照表（見 docs/architecture/connection-layer.md 第 6.2 節）
var connectionDirectory = builder.AddRedis("connection-directory");

// 訊息匯流排：Gateway <-> Dispatcher 之間的 NATS pub/sub（dispatch.deliver / connect.deliver.{nodeId}）
var messageBus = builder.AddNats("message-bus");

builder.AddProject<Projects.Dispatcher>("dispatcher")
    .WithReference(connectionDirectory)
    .WithReference(messageBus)
    .WaitFor(connectionDirectory)
    .WaitFor(messageBus);

// 多開複本模擬多個 Gateway 節點，驗證 ConnectionDirectory + Dispatcher 的跨節點路由
builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(connectionDirectory)
    .WithReference(messageBus)
    .WaitFor(connectionDirectory)
    .WaitFor(messageBus)
    .WithReplicas(2);

builder.Build().Run();
