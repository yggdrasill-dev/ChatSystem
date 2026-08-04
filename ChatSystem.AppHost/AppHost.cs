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

// 房間層自己的 Redis：房間與封鎖名單是持久資料（ADR-7 標為 provisional，將來跟聊天層的
// 訊息記錄一起遷到正式儲存），成員名單是暫時狀態但要跟房間資料跨 key 一起操作。
var roomStore = builder.AddRedis("room-store")
	.WithDataVolume()
	.WithPersistence();

// 協定層：訂閱 command.inbound，解析 client 命令後分派給各層註冊的 handler
builder.AddProject<Projects.CommandRouter>("command-router")
	.WithReference(connectionDirectory)
	.WithReference(messageBus)
	.WithReference(roomStore)
	.WithReference(identityStore)
	.WaitFor(connectionDirectory)
	.WaitFor(messageBus)
	.WaitFor(roomStore)
	.WaitFor(identityStore);

// 多開複本模擬多個 Gateway 節點，驗證 ConnectionDirectory + Dispatcher 的跨節點路由
// 前端的 BFF：出靜態檔（未來 Angular 的 build 產物）並簽發 session cookie。
// 跟 Gateway 同 site 所以 cookie 帶得過去；需要 identity-store（Session/nonce/profile）
// 與 message-bus（登出時終止該身分的連線）。
builder.AddProject<Projects.WebBff>("web-bff")
	.WithReference(identityStore)
	.WithReference(messageBus)
	.WaitFor(identityStore)
	.WaitFor(messageBus)
	.WithExternalHttpEndpoints();

builder.AddProject<Projects.Gateway>("gateway")
	.WithReference(connectionDirectory)
	.WithReference(messageBus)
	.WithReference(identityStore)
	.WaitFor(connectionDirectory)
	.WaitFor(messageBus)
	.WaitFor(identityStore)
	.WithReplicas(2);

builder.Build().Run();
