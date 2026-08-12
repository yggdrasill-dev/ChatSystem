using CommandRouter;
using Common.Protocol;
using Common.Rooms;
using Common.Storage;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

var builder = Host.CreateApplicationBuilder(args);
{
	builder.AddServiceDefaults();

	// 下行要用 IOutboundGateway（回訊息），它經 Dispatcher，所以這裡需要 ConnectionDirectory
	// 的 Redis 與訊息匯流排。
	//
	// 刻意沒有 AddConnectionTerminator()：協定層唯一的終止路徑是 IInboundFilter 的
	// Terminate，那個機制已經移除（protocol-layer.md ADR-6），所以這個 process 不再有任何
	// 關掉使用者連線的能力。終止連線目前只剩身分層的 Supersede 在用。
	builder.AddRedisClient("connection-directory");
	builder.AddNatsClient("message-bus");
	builder.Services.AddConnectionDirectory();
	builder.Services.AddOutboundGateway();

	// 協定層本身：subject ↔ 型別的對應表與出口
	builder.Services.AddPacketRegistry();

	// 房間層橫跨兩個儲存：房間與封鎖名單在 Postgres（chat-db），成員名單留 Redis（room-store）。
	// fan-out 要把 userId 換成 connectionId，所以也需要身分層的 IPresenceDirectory（identity-store）。
	//
	// room-store / identity-store 是**邏輯名稱**，AppHost 目前把它們指向同一顆實體 Redis
	// ——所以這裡兩個 keyed client 連的是同一台，那是刻意的（見 AppHost.cs 的註解）。
	// **註冊的是 DbContext factory 而不是 DbContext**：三個 store 與 ChatDbMigrator 都是 singleton
	// （其中 ChatRetentionSweeper 還是 BackgroundService），而 DbContext 是 scoped 且非執行緒安全。
	// Aspire 的 AddNpgsqlDbContext 負責連線字串、health check 與 telemetry；工廠讓 singleton 拿得到
	// 短命的 context，形狀跟先前的 NpgsqlDataSource 一樣。
	//
	// **工廠必須自己帶 UseNpgsql。** `AddDbContextFactory` 不給 optionsAction 時會註冊一份
	// **沒有 provider** 的 DbContextOptions，而它蓋掉 Aspire 剛註冊的那一份——建出來的 context
	// 一用就丟「No database provider has been configured」，migrator 是第一個踩到的人。
	builder.AddNpgsqlDbContext<ChatDbContext>("chat-db");
	builder.Services.AddDbContextFactory<ChatDbContext>(
		options => options.UseNpgsql(builder.Configuration.GetConnectionString("chat-db")),
		ServiceLifetime.Singleton);
	builder.AddKeyedRedisClient("room-store");
	builder.AddKeyedRedisClient("identity-store");
	// chat-ratelimit：每人每秒的訊息計數（ADR-7）。又一個邏輯名稱指向同一顆實體 Redis。
	builder.AddKeyedRedisClient("chat-ratelimit");
	// 房間層的兩半分成兩行，因為它們真的是兩個儲存：AddChatDb() 給 Postgres 上的房間、封鎖名單
	// 與訊息（三張表由兩層共用），AddRoomMembership() 給 Redis 上的成員名單。
	builder.Services.AddChatDb();
	builder.Services.AddRoomMembership("room-store");
	builder.Services.AddIdentityStores("identity-store");
	builder.Services.AddRoomPackets();
	builder.Services.AddRoomMembershipMaintenance();

	// 聊天層：訊息收發與歷史查詢。重用房間層的 RoomBroadcaster 與 IRoomMembership
	// （chat-layer.md 6.8），以及身分層的 IUserProfileStore 讀顯示名稱快照（ADR-3），
	// 所以必須排在上面那幾行之後。
	//
	// 訊息的儲存已經由上面的 AddChatDb() 註冊（跟房間資料同一個 chat-db），所以這裡是兩行：
	// AddChatCore() 是程序內的設定與發號，AddChatRateLimiting() 是 Redis 上的計數。**分兩行是
	// 刻意的**——聊天層要一顆 Redis 這件事就跟房間層橫跨兩個儲存一樣，值得在這裡直接看得見。
	builder.Services.AddChatCore();
	builder.Services.AddChatRateLimiting("chat-ratelimit");
	builder.Services.AddChatPackets();
	builder.Services.AddChatRetention();

	// 這個 process 唯一一次 Adaptare message queue 註冊（見 Common/NatsMessagingRegistration.cs）。
	builder.Services
		.AddMessageQueue()
		.AddNatsGlobPatternExchange("*")
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
			// processor 與 handler 在同一條鏈裡共存沒有問題（實測過兩者都會被呼叫）。用哪一種
			// 取決於語意：command.inbound 要回 ack 所以是 processor，斷線事件是
			// fire-and-forget 所以是 handler。
			.AddProcessor<InboundProcessor>("command.inbound", "command.inbound")
			.AddHandler<RoomDisconnectHandler>(RoomDisconnectHandler.Subject, RoomDisconnectHandler.QueueGroup));
}

var host = builder.Build();
{
	// **schema 要在開始收訊息之前就位。** 不做成 hosted service 的理由見 AddChatDb()：
	// 同一批 hosted service 裡有會立刻開始收訊息的東西，而 hosted service 的順序是註冊順序。
	// 這裡明確 await，失敗就啟動失敗——比第一則命令進來才炸在 SQL 上好查。
	//
	// B1 之後跑的是 EF Core Migrations（`Common/Storage/Migrations/`），重跑會跳過已套用的。
	await host.Services.GetRequiredService<ChatDbMigrator>().MigrateAsync();

	// 在這裡解析 PacketRegistry 有兩個目的：把對應表印進 log 方便排查，以及讓重複註冊的
	// fail fast 發生在啟動時而不是第一則訊息進來時（ADR-4 失去編譯期檢查的補償）。
	var registry = host.Services.GetRequiredService<PacketRegistry>();

	host.Services.GetRequiredService<ILogger<Program>>().LogInformation(
		"Registered {SubjectCount} subject(s): {Subjects}",
		registry.Subjects.Count,
		registry.Subjects.Count == 0 ? "(none)" : string.Join(", ", registry.Subjects));
}

host.Run();
