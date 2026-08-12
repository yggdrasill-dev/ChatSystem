using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Net.WebSockets;
using Aspire.Hosting.Testing;
using Chat.Protos;
using Common.Chat;
using Common.Rooms;
using Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

namespace E2E.Tests;

// 啟動真的 AppHost 一次，整組測試共用。
//
// 為什麼要有這一組測試：`Integration.Tests` 跑在 Adaptare.Direct 上、0.6 秒跑完，功能面已經
// 蓋得比這裡密。但有三件事它結構上蓋不到，而三個真實 bug 全都落在那三件事裡：
//   1. **production 的 messaging 接線**——每個 Program.cs 那條 AddNatsMessageQueue 鏈綁 transport，
//      換不成 Direct。重複註冊、handler 沒掛上，都只在這裡看得見。
//   2. **wire format**——Direct 傳的是同一個物件參考，不會像 NATS 把 0 bytes 變成 null。
//   3. **跨節點 fan-out**——一個 process 只有一個 Direct queue，跨 Gateway 節點的投遞無法模擬。
// 所以這裡刻意偏重那三件事，不重複 Integration.Tests 已經釘住的每一條 handler 行為。
public sealed class AppHostFixture : IAsyncLifetime
{
	// 容器可能要下載，第一次跑給寬鬆一點。
	private static readonly TimeSpan _StartupTimeout = TimeSpan.FromMinutes(5);

	private DistributedApplication? m_App;

	// command-router 的 log，**從 AppHost 一啟動就在背景收**。不能等失敗了才訂閱：那個 process
	// 啟動失敗時 log stream 跟著關掉，事後 WatchAsync 只會一路等到逾時（實際踩過，那一版診斷
	// 印的是「讀 log 逾時」——比沒有還糟，因為它看起來像診斷有跑）。
	private readonly List<string> m_CommandRouterLog = [];

	private CancellationTokenSource? m_LogWatch;

	public DistributedApplication App => m_App ?? throw new InvalidOperationException("AppHost 沒有啟動。");

	public Uri GatewayHttp { get; private set; } = null!;

	public Uri WebBffHttp { get; private set; } = null!;

	// **這裡問的是實體資源的名字，不是各層用的邏輯名稱。** AppHost 現在只開一顆 Redis、把
	// connection-directory / room-store / identity-store 這些**連線字串名稱**都指向它，那些名稱
	// 活在各專案的設定裡、不是 AppHost 的資源圖，所以 GetConnectionStringAsync("room-store")
	// 會拿不到東西。這個 fixture 跟部署拓樸綁在一起是刻意的——它要直接對 Redis 說話。
	private string RedisConnectionString { get; set; } = null!;

	private string ChatDbConnectionString { get; set; } = null!;

	public async Task InitializeAsync()
	{
		// 閘門關著就不要花一分鐘啟容器——所有測試都會被 Skip，fixture 也不該做事。
		if (!E2EFactAttribute.Enabled)
			return;

		var builder = await DistributedApplicationTestingBuilder
			.CreateAsync<Projects.ChatSystem_AppHost>()
			.ConfigureAwait(false);

		// 開發用的假登入後門有兩道鎖，這裡兩道都要處理：
		//   - Login:AllowFakeIdTokens：appsettings 裡刻意留 false，只在這個 process 的環境變數打開，
		//     所以 repo 裡不會有一份「後門是開的」設定檔
		//   - Development 環境：同時也是 Gateway 讀到 Origin allowlist 的條件
		//     （allowlist 在 appsettings.Development.json；空 allowlist 是 fail-closed，會全部 403）
		Configure(builder, "web-bff", ("Login__AllowFakeIdTokens", "true"));
		Configure(builder, "gateway");

		m_App = await builder.BuildAsync().ConfigureAwait(false);
		await m_App.StartAsync().ConfigureAwait(false);

		StartWatchingCommandRouterLog();

		GatewayHttp = m_App.GetEndpoint("gateway", "http");
		WebBffHttp = m_App.GetEndpoint("web-bff", "http");
		RedisConnectionString =
			await m_App.GetConnectionStringAsync("redis").ConfigureAwait(false)
			?? throw new InvalidOperationException("拿不到 redis 的連線字串。");
		ChatDbConnectionString =
			await m_App.GetConnectionStringAsync("chat-db").ConfigureAwait(false)
			?? throw new InvalidOperationException("拿不到 chat-db 的連線字串。");

		// 不用 WaitForResourceAsync：Gateway 開了 replica，資源名稱會變成 gateway-0/gateway-1 之類的
		// 衍生名字，猜名字比直接問傳輸層脆弱。這裡直接打 endpoint，能回應就是真的可以用了。
		//
		// **但要知道這個檢查證明不了什麼**：GatewayHttp 是 Aspire 放在 replica 前面的 proxy，
		// 一次成功的 GET 只代表**至少一個** replica 會回應，分不出後面站著一個還是兩個。所以
		// 「兩個 replica 都在服務」不是這個 fixture 給的保證——需要那個性質的測試要自己確認，
		// 見 RoomFlowE2ETests.ConnectToAnotherNodeAsync。這個限制曾經讓
		// Broadcast_ReachesAMemberOnADifferentGatewayNode 隨啟動時序紅掉。
		await WaitUntilReachableAsync(GatewayHttp, "/").ConfigureAwait(false);
		await WaitUntilReachableAsync(WebBffHttp, "/login/nonce").ConfigureAwait(false);

		// **Gateway 就緒不代表命令有人處理。** 上面兩個檢查涵蓋不到 CommandRouter——它是 worker，
		// 沒有 endpoint 可以打——而它在開始訂閱 command.inbound 之前要先 await 一次 migration
		// （見 CommandRouter/Program.cs）。那段期間 client 連得上、命令發得出去、**什麼都不會回來**。
		//
		// 這個洞一直都在，B1 只是把它推過了臨界點：migration 多建一張 messages 表與一個索引，
		// 於是第一條房間測試開始隨機在「等不到 room.reply」上紅掉，而每一次紅都是假警報。
		// 這跟 Broadcast_ReachesAMemberOnADifferentGatewayNode 那次是同一類問題——**就緒假設**，
		// 不是產品。
		await WaitUntilCommandsAreServedAsync().ConfigureAwait(false);
	}

	public async Task DisposeAsync()
	{
		if (m_LogWatch is not null)
			await m_LogWatch.CancelAsync().ConfigureAwait(false);

		if (m_App is not null)
			await m_App.DisposeAsync().ConfigureAwait(false);
	}

	public HttpClient CreateWebBffClient() =>
		// 明確指定 "http"：這些專案 http/https 兩個 endpoint 都有，不指定會分不出要哪一個。
		// 用 http 是刻意的——session cookie 帶 Secure，走 https 時 CookieContainer 的行為會多一層
		// 變數，而我們本來就打算自己從 Set-Cookie 把 token 抽出來（見 ChatClient）。
		App.CreateHttpClient("web-bff", "http");

	// connectionId -> nodeId 的快照。**現在掃的是共用的 keyspace**（AppHost 只有一顆 Redis），所以
	// `Conn:*` 這個 pattern 不再只是效率問題而是正確性的一部分；而且連線目錄現在也跟著被持久化，
	// 上一輪跑剩的 `Conn:*` 會留下來——差集的寫法本來就免疫（殘留也在 before 裡），但別改成數總數。
	// key 前綴跟 RedisConnectionDirectory.Key 綁在一起（`Conn:{id}`），
	// 那是 internal 的實作細節，改了這裡要一起改——換來的是「兩條連線真的落在不同節點」可以被
	// 精確斷言，而不是靠「廣播收到了」去推論。
	public async Task<IReadOnlyDictionary<string, string>> SnapshotConnectionsAsync()
	{
		await using var redis = await ConnectionMultiplexer
			.ConnectAsync(RedisConnectionString)
			.ConfigureAwait(false);

		var database = redis.GetDatabase();
		var snapshot = new Dictionary<string, string>();

		foreach (var key in redis.GetServer(redis.GetEndPoints()[0]).Keys(pattern: "Conn:*"))
		{
			var nodeId = await database.StringGetAsync(key).ConfigureAwait(false);

			if (!nodeId.IsNullOrEmpty)
				snapshot[key.ToString()] = nodeId.ToString();
		}

		return snapshot;
	}

	// 真 Postgres 上的房間層儲存，**建在一個獨立的 schema 裡**。
	//
	// 契約測試每一條都要一個空的 store，而它跑的是應用真正在用的那個資料庫——直接
	// TRUNCATE public.rooms 會把同一組 E2E 其他測試建的房間一起清掉。用 search_path 隔離：
	// migration 產生的 DDL 刻意不寫 schema 名稱，所以同一份 DDL 建到哪裡由連線決定。
	// `__EFMigrationsHistory` 也跟著落在那個 schema 裡，所以兩邊的 migration 狀態互不干擾。
	public async Task<ChatDbProbe> ConnectChatDbAsync()
	{
		var connectionString =
			new NpgsqlConnectionStringBuilder(ChatDbConnectionString) { SearchPath = ChatDbProbe.Schema }
				.ConnectionString;

		// 這一步不能省：search_path 指向一個不存在的 schema，Postgres **不會報錯**，只會讓後面的
		// CREATE TABLE 落到 public 去——那正是要避免的事，而且它會靜默地看起來一切正常。
		await using (var connection = new NpgsqlConnection(connectionString))
		{
			await connection.OpenAsync().ConfigureAwait(false);

			await using var command = connection.CreateCommand();
			// **先 DROP 再 CREATE**：chat-db 掛了 data volume，schema 會跨執行留下來。EF Migrations
			// 會因為 `__EFMigrationsHistory` 說「都套用過了」而**整份跳過**——改了 DDL 也不會生效，
			// 跟手寫 `CREATE TABLE IF NOT EXISTS` 時代同一個陷阱，只是換了一個機制。
			// 那會讓「拿掉 ON DELETE CASCADE」這種變異驗不出來（實際踩過）。
			command.CommandText =
				$"DROP SCHEMA IF EXISTS {ChatDbProbe.Schema} CASCADE; CREATE SCHEMA {ChatDbProbe.Schema}";
			await command.ExecuteNonQueryAsync().ConfigureAwait(false);
		}

		// `PooledDbContextFactory` 是 EF 現成的 IDbContextFactory 實作，形狀跟 CommandRouter 那邊
		// 由 DI 給的一樣——契約測試因此跑在跟正式路徑相同的 context 生命週期上。
		var factory = new PooledDbContextFactory<ChatDbContext>(
			new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(connectionString).Options);

		await new ChatDbMigrator(factory).MigrateAsync().ConfigureAwait(false);

		return new ChatDbProbe(factory);
	}

	// 直接連上那顆真 Redis。給限流的契約測試用（RedisContractTests）。
	//
	// **跟 ConnectChatDbAsync 是同一個角色，但沒有隔離機制**：Redis 沒有 schema，而 FLUSHDB 會把
	// 正在跑的 app 的 session、連線目錄、成員名單一起清掉。隔離因此只能由 key 本身負責——限流的
	// key 帶著 unixSecond，所以「每條測試落在不同的秒」就夠了，見 ChatRateLimiterContract。
	public async Task<IConnectionMultiplexer> ConnectRedisAsync() =>
		await ConnectionMultiplexer.ConnectAsync(RedisConnectionString).ConfigureAwait(false);

	private static void Configure(
		IDistributedApplicationBuilder builder,
		string resourceName,
		params (string Key, string Value)[] environment)
	{
		var resource = builder.Resources.OfType<ProjectResource>().Single(r => r.Name == resourceName);
		var resourceBuilder = builder.CreateResourceBuilder(resource);

		resourceBuilder.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

		foreach (var (key, value) in environment)
			resourceBuilder.WithEnvironment(key, value);
	}

	private static async Task WaitUntilReachableAsync(Uri baseUri, string path)
	{
		using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) };

		var deadline = DateTime.UtcNow + _StartupTimeout;
		Exception? last = null;

		while (DateTime.UtcNow < deadline)
		{
			try
			{
				using var response = await http.GetAsync(path).ConfigureAwait(false);

				if (response.IsSuccessStatusCode)
					return;
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				last = ex;
			}

			await Task.Delay(500).ConfigureAwait(false);
		}

		throw new TimeoutException($"{baseUri}{path} 在 {_StartupTimeout} 內沒有起來。", last);
	}

	// 走一次真正的往返來確認整條命令鏈通了：Gateway → NATS → CommandRouter → 回程。這跟上面兩個
	// 檢查的哲學一樣——**直接問傳輸層，不猜資源名字、不猜內部狀態**。
	//
	// 用 `room.list` 當探針是因為它是唯一沒有副作用的命令：不需要先建房、不需要人在房間裡、
	// 不會廣播給任何人，所以它留給後面測試的痕跡只有一條連線與一個 session。
	//
	// **每一輪重連而不是重送**：ack 沒回來的話 Gateway 會在 10 秒後關掉那條連線
	// （protocol-layer.md ADR-2），所以重試不能重用同一個 socket。
	private async Task WaitUntilCommandsAreServedAsync()
	{
		// **獨立的、比 _StartupTimeout 短的期限**：到這一步容器都已經在跑了（上面兩個 HTTP 檢查
		// 過了），剩下只等一個 process 跑完 migration 並開始訂閱。等不到通常代表它**崩了**，
		// 而不是還在暖機——那種情況等五分鐘只是把答案延後五分鐘。
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
		Exception? last = null;

		while (DateTime.UtcNow < deadline)
		{
			try
			{
				await using var probe = await ChatClient
					.ConnectAsync(this, "readiness-probe")
					.ConfigureAwait(false);

				await probe.SendAsync("room.list", new ListRoomsRequest()).ConfigureAwait(false);
				await probe
					.ExpectAsync("room.list.reply", RoomList.Parser, TimeSpan.FromSeconds(2))
					.ConfigureAwait(false);

				return;
			}
			catch (Exception ex) when (ex is TimeoutException or WebSocketException or HttpRequestException)
			{
				last = ex;
			}
		}

		// **失敗時附上 chat-db 的實際狀態。** CommandRouter 起不來最可能的原因是 migration 失敗，
		// 而它的 stderr 在 Aspire 的 log 裡、測試看不到——沒有這段的話這個例外只會說「沒回應」，
		// 那是症狀不是原因。schema 的實際內容分得出「表根本沒建起來」與「表在那裡但 EF 不認得」。
		throw new TimeoutException(
			$"""
			CommandRouter 在 60 秒內沒有開始處理命令。
			chat-db 的 public schema：{await DescribeChatDbAsync()}
			command-router 的最後幾行 log：
			{TailCommandRouterLog()}
			""",
			last);
	}

	// **Aspire 的 resource log 不會流進測試輸出**，所以一個 process 啟動失敗時，測試看到的只有
	// 「沒回應」。CommandRouter 沒有 endpoint 可打，log 是唯一問得到它的地方。
	private void StartWatchingCommandRouterLog()
	{
		m_LogWatch = new CancellationTokenSource();

		var logs = App.Services.GetRequiredService<ResourceLoggerService>();
		var token = m_LogWatch.Token;

		_ = Task.Run(
			async () =>
			{
				try
				{
					await foreach (var batch in logs.WatchAsync("command-router").WithCancellation(token))
					{
						lock (m_CommandRouterLog)
							m_CommandRouterLog.AddRange(batch.Select(line => line.Content));
					}
				}
				catch (OperationCanceledException)
				{
					// 收尾，不是錯誤。
				}
			},
			token);
	}

	private string TailCommandRouterLog()
	{
		lock (m_CommandRouterLog)
		{
			return m_CommandRouterLog.Count == 0
				? "(沒有任何 log)"
				: string.Join(Environment.NewLine, m_CommandRouterLog.TakeLast(30));
		}
	}

	private async Task<string> DescribeChatDbAsync()
	{
		try
		{
			await using var connection = new NpgsqlConnection(ChatDbConnectionString);
			await connection.OpenAsync().ConfigureAwait(false);

			await using var command = connection.CreateCommand();
			// **同時問「有哪些表」與「EF 認為套用了哪些 migration」。** 只問前者會被誤導：
			// `__EFMigrationsHistory` 存在不代表 migration 成功——EF 是先建那張表、再套用第一個
			// migration，所以「表在、history 是空的」正是套用失敗的樣子。
			command.CommandText =
				"""
				SELECT
					coalesce((
						SELECT string_agg(table_name, ', ' ORDER BY table_name)
						FROM information_schema.tables WHERE table_schema = 'public'
					), '(沒有任何表)')
					|| ' / 已套用的 migration：'
					|| coalesce((
						SELECT string_agg("MigrationId", ', ' ORDER BY "MigrationId")
						FROM "__EFMigrationsHistory"
					), '(一個都沒有)')
				""";

			return (await command.ExecuteScalarAsync().ConfigureAwait(false))?.ToString() ?? "(查不到)";
		}
		catch (Exception ex)
		{
			return $"(連不上：{ex.Message})";
		}
	}
}

// 真 Postgres 上的 IRoomStore / IRoomBanList / IChatMessageStore，活在自己的 schema 裡。三個實作
// 都是 internal，靠 Common.csproj 的 InternalsVisibleTo 拿得到。
public sealed class ChatDbProbe(IDbContextFactory<ChatDbContext> dbFactory) : IAsyncDisposable
{
	public const string Schema = "contract_probe";

	public IRoomStore Store { get; } = new PostgresRoomStore(dbFactory);

	public IRoomBanList Bans { get; } = new PostgresRoomBanList(dbFactory);

	public IChatMessageStore Messages { get; } = new PostgresChatMessageStore(dbFactory);

	// `CASCADE` 這裡指的是 TRUNCATE 的 CASCADE（連帶清掉有 FK 指過來的 room_bans 與 messages），
	// 跟 ADR-10 那個 ON DELETE CASCADE 是兩件不同的事，只是剛好同名。
	public async Task ResetAsync()
	{
		await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

		await db.Database.ExecuteSqlRawAsync("TRUNCATE rooms CASCADE").ConfigureAwait(false);
	}

	// 連線由 Npgsql 那個以連線字串為 key 的**全域**池管，每個 probe 各自 dispose 不會累積連線
	// ——所以這裡沒有東西要釋放。留著 IAsyncDisposable 是因為測試類別的 DisposeAsync 已經在呼叫它，
	// 而「probe 有生命週期」這件事之後可能會再需要。
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>
{
	public const string Name = "apphost";
}
