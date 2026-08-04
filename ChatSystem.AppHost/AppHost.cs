var builder = DistributedApplication.CreateBuilder(args);

// ConnectionDirectory：連線層自己擁有的 ConnectionId -> NodeId 對照表
var connectionDirectory = builder.AddRedis("connection-directory");

// 訊息匯流排：Gateway <-> Dispatcher 之間的 NATS pub/sub（dispatch.deliver / connect.deliver.{nodeId}）
var messageBus = builder.AddNats("message-bus");

// 身分層自己的 Redis：Session 是持久資料（7 天），跟 connection-directory 那個純快取用途的
// 需求不同，所以開持久化並掛 volume——不然 AppHost 重啟一次就得重新登入。
var identityStore = builder.AddRedis("identity-store")
	.WithDataVolume()
	.WithPersistence();

builder.AddProject<Projects.Dispatcher>("dispatcher")
	.WithReference(connectionDirectory)
	.WithReference(messageBus)
	.WaitFor(connectionDirectory)
	.WaitFor(messageBus);

// 協定層：訂閱 command.inbound，解析 client 命令後分派給各層註冊的 handler
builder.AddProject<Projects.CommandRouter>("command-router")
	.WithReference(connectionDirectory)
	.WithReference(messageBus)
	.WaitFor(connectionDirectory)
	.WaitFor(messageBus);

// 多開複本模擬多個 Gateway 節點，驗證 ConnectionDirectory + Dispatcher 的跨節點路由
builder.AddProject<Projects.Gateway>("gateway")
	.WithReference(connectionDirectory)
	.WithReference(messageBus)
	.WithReference(identityStore)
	.WaitFor(connectionDirectory)
	.WaitFor(messageBus)
	.WaitFor(identityStore)
	.WithReplicas(2);

builder.Build().Run();
