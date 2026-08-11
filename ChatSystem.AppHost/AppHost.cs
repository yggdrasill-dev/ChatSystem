var builder = DistributedApplication.CreateBuilder(args);

// **一顆實體 Redis，多個邏輯名稱指向它。**
//
// 各層拿到的是「邏輯名稱」而不是實體資源：`AddRoomMembership(redisServiceKey)` 這類擴充方法收的是
// service key，每個 Program.cs 才把它綁到一個連線字串名稱。所以「開幾顆 Redis」從來不是程式碼
// 結構問題——是這個檔案的部署參數。要拆回多顆就是把下面 WithReference 的第一個引數換成不同資源，
// 層與層的程式碼一行都不用動。
//
// 現在只開一顆，因為這個專案的規模撐不起（也不需要）多顆。**代價要記著**：
//   1. persistence 設定被拉到最嚴格的那一個。身分層的 Session 要活過重啟，所以整顆開 volume +
//      持久化，連線目錄那些純快取資料也跟著被寫進磁碟。無害，但那是合併的實際成本。
//   2. **key 前綴從「剛好不撞」變成「必須不撞」。** 目前用掉的（新增前綴前先對一遍這份清單）：
//      `Conn:`（連線層）、`{rooms}:`（房間層的房間／封鎖／成員／寬限期）、
//      `Session:`／`Presence:`／`LoginNonce:`／`Profile:`（身分層）。
//   3. 每個邏輯名稱各自建一個 ConnectionMultiplexer，所以是 N 組連線池連到同一台。量小無所謂。
//
// 拆開的判準（也是唯一該拆的理由）：這些**instance 級**的設定各層需不需要不一樣——
// maxmemory-policy（淘汰誰）、persistence、以及「一個慢指令卡住所有 client」的故障範圍。
// key 前綴解決撞名，解決不了那三個。
var redis = builder.AddRedis("redis")
	.WithDataVolume()
	.WithPersistence();

// 訊息匯流排：Gateway <-> Dispatcher 之間的 NATS pub/sub（dispatch.deliver / connect.deliver.{nodeId}）
var messageBus = builder.AddNats("message-bus");

// 正式持久儲存（chat-layer.md ADR-4）：rooms / room_bans / messages 三張表同一個資料庫，
// 理由是同一套 migration 與備份策略。Session / Presence / 成員名單**不遷**——它們是 TTL 與
// compare-and-swap 語意，放進關聯式資料庫會變難看也變慢。
//
// 跟上面 Redis 那顆相反，這裡實體與邏輯是 1:1：只有一個資料庫、也只有一個名稱。要拆的時候
// 面對的是 schema 而不是連線字串，所以那層 indirection 在這裡買不到東西。
var chatDb = builder.AddPostgres("postgres")
	.WithDataVolume()
	.AddDatabase("chat-db");

builder.AddProject<Projects.Dispatcher>("dispatcher")
	// connection-directory：連線層自己擁有的 ConnectionId -> NodeId 對照表
	.WithReference(redis, "connection-directory")
	.WithReference(messageBus)
	.WaitFor(redis)
	.WaitFor(messageBus);

// 協定層：訂閱 command.inbound，解析 client 命令後分派給各層註冊的 handler
builder.AddProject<Projects.CommandRouter>("command-router")
	.WithReference(redis, "connection-directory")
	// room-store：房間、封鎖名單、成員名單。前兩者在階段 B 會搬去 Postgres，這個名稱屆時只剩
	// 成員名單——但那不需要動這裡，因為名稱本來就跟實體無關。
	.WithReference(redis, "room-store")
	// identity-store：Session（7 天 TTL）、登入 nonce、profile、Presence
	.WithReference(redis, "identity-store")
	// chat-db：房間、封鎖名單（階段 B）與訊息（階段 B 的下一步）
	.WithReference(chatDb)
	.WithReference(messageBus)
	.WaitFor(redis)
	.WaitFor(chatDb)
	.WaitFor(messageBus);

// 前端的 BFF：出靜態檔（未來 Angular 的 build 產物）並簽發 session cookie。
// 跟 Gateway 同 site 所以 cookie 帶得過去；需要 identity-store（Session/nonce/profile）
// 與 message-bus（登出時終止該身分的連線）。
builder.AddProject<Projects.WebBff>("web-bff")
	.WithReference(redis, "identity-store")
	.WithReference(messageBus)
	.WaitFor(redis)
	.WaitFor(messageBus)
	.WithExternalHttpEndpoints();

// 多開複本模擬多個 Gateway 節點，驗證 ConnectionDirectory + Dispatcher 的跨節點路由
builder.AddProject<Projects.Gateway>("gateway")
	.WithReference(redis, "connection-directory")
	.WithReference(redis, "identity-store")
	.WithReference(messageBus)
	.WaitFor(redis)
	.WaitFor(messageBus)
	.WithReplicas(2);

builder.Build().Run();
