using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Common.Rooms;
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

	public DistributedApplication App => m_App ?? throw new InvalidOperationException("AppHost 沒有啟動。");

	public Uri GatewayHttp { get; private set; } = null!;

	public Uri WebBffHttp { get; private set; } = null!;

	// **這裡問的是實體資源的名字，不是各層用的邏輯名稱。** AppHost 現在只開一顆 Redis、把
	// connection-directory / room-store / identity-store 這些**連線字串名稱**都指向它，那些名稱
	// 活在各專案的設定裡、不是 AppHost 的資源圖，所以 GetConnectionStringAsync("room-store")
	// 會拿不到東西。這個 fixture 跟部署拓樸綁在一起是刻意的——它要直接對 Redis 說話。
	private string RedisConnectionString { get; set; } = null!;

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

		GatewayHttp = m_App.GetEndpoint("gateway", "http");
		WebBffHttp = m_App.GetEndpoint("web-bff", "http");
		RedisConnectionString =
			await m_App.GetConnectionStringAsync("redis").ConfigureAwait(false)
			?? throw new InvalidOperationException("拿不到 redis 的連線字串。");

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
	}

	public async Task DisposeAsync()
	{
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

	// 直接對 room-store 說話的房間層儲存。**只給「經由 client 命令走不到」的斷言用。**
	//
	// 目前有兩條那樣的性質，兩條都是 ADR-10 帶進來的：TryUpdateSettingsAsync 的 EXISTS 守門
	// 擋的是 update 與 delete 交錯的那個窗口，而 RoomUpdateHandler 自己會先 GetAsync，所以
	// 循序的 client 命令永遠走不到那條路徑；而「刪房帶走封鎖名單」在 in-memory 的替身上湊不
	// 出來（兩個替身是獨立物件），契約測試因此蓋不到。
	public async Task<RoomStoreProbe> ConnectRoomStoreAsync() =>
		new(await ConnectionMultiplexer.ConnectAsync(RedisConnectionString).ConfigureAwait(false));

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
}

// 真 Redis 上的 IRoomStore / IRoomBanList。兩個實作都是 internal，靠 Common.csproj 的
// InternalsVisibleTo 拿得到。
public sealed class RoomStoreProbe(ConnectionMultiplexer redis) : IAsyncDisposable
{
	public IRoomStore Store { get; } = new RedisRoomStore(redis);

	public IRoomBanList Bans { get; } = new RedisRoomBanList(redis);

	public ValueTask DisposeAsync() => redis.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>
{
	public const string Name = "apphost";
}
