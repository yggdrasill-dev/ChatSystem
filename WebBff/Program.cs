using NATS.Client.Core;
using WebBff.Services;

var builder = WebApplication.CreateBuilder(args);

// Google client id 沒設定時不讓整個 process 起不來：其他四個服務要能繼續跑，
// 而漏設定的症狀被限縮在「登不進去」（POST /login 回 503）加啟動時的一行 Warning。
var googleClientId = builder.Configuration["Login:GoogleClientId"];

// 開發用的身分偽造後門，兩道鎖都要開才生效。見 FakeIdTokenValidator。
var allowFakeIdTokens = builder.Environment.IsDevelopment()
	&& builder.Configuration.GetValue("Login:AllowFakeIdTokens", false);

{
	builder.AddServiceDefaults();

	// 身分層自己的 Redis：Session / nonce / profile 都在這裡
	builder.AddKeyedRedisClient("identity-store");
	builder.Services.AddIdentityStores("identity-store");

	// 登出要主動終止該身分的連線，經 Dispatcher，所以需要訊息匯流排。
	// 不需要 connection-directory——查節點是 Dispatcher 的事。
	builder.AddNatsClient("message-bus");
	builder.Services.AddConnectionTerminator();
	builder.Services.AddConnectionAuthenticator();

	// 這個 process 唯一一次 Adaptare message queue 註冊。它只發不收（登出時的 terminate），
	// 所以沒有任何 handler（見 Common/NatsMessagingRegistration.cs）。
	builder.Services
		.AddMessageQueue()
		.AddNatsGlobPatternExchange("*")
		.AddNatsMessageQueue(config => config
			.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()));

	if (allowFakeIdTokens)
		builder.Services.AddSingleton<IIdTokenValidator, FakeIdTokenValidator>();
	else
		builder.Services.AddSingleton<IIdTokenValidator>(new GoogleIdTokenValidator(googleClientId));
}

var app = builder.Build();
{
	var logger = app.Services.GetRequiredService<ILogger<Program>>();

	if (allowFakeIdTokens)
		logger.LogWarning(
			"Login:AllowFakeIdTokens is ON. Any string is accepted as an identity. Development only.");
	else if (string.IsNullOrEmpty(googleClientId))
		logger.LogWarning(
			"Login:GoogleClientId is not configured, so POST /login will return 503 until it is set.");

	app.MapDefaultEndpoints();

	app.MapLoginEndpoints();

	// 前端目前只有一個佔位頁。之後不論選哪套工具鏈，build 產物落到 wwwroot 就能用；
	// fallback 是給 SPA 深層路由（/rooms/xxx 重新整理）準備的。
	app.UseDefaultFiles();
	app.UseStaticFiles();
	app.MapFallbackToFile("index.html");
}

app.Run();
