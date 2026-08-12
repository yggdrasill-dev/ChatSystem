using Common.Chat;
using Common.Tests.Chat;
using StackExchange.Redis;

namespace E2E.Tests;

// **限流的契約在這裡對著真 Redis 跑一次。** 跟 PostgresContractTests 完全同一個安排（那個檔案
// 開頭那三段說明——為什麼放在 E2E、為什麼閘門是編譯期的——一字不差地適用於這一份），差別只有
// 兩個：這裡借的容器是 Redis 而不是 Postgres，而且**沒有 schema 可以隔離**，隔離由 key 負責。
[Collection(AppHostCollection.Name)]
public sealed class RedisChatRateLimiterContractTests(AppHostFixture fixture) : ChatRateLimiterContract, IAsyncLifetime
{
	// 每一次 NewWindowStart() 往前跳一天。**這是這一份跟 Postgres 那三個殼唯一的結構差異**：
	// 那邊每條測試前 TRUNCATE 一次，這裡不能清 keyspace（會清掉正在跑的 app 的狀態），所以改成
	// 讓每條測試的 key 天生不會撞——key 是 `Chat:rate:{userId}:{unixSecond}`，而秒數由假時鐘決定。
	//
	// 跨執行也安全：key 的 TTL 是兩秒，上一輪留下來的早就不在了。
	private static long s_Windows;

	private IConnectionMultiplexer m_Redis = null!;

	public async Task InitializeAsync() => m_Redis = await fixture.ConnectRedisAsync();

	public async Task DisposeAsync() => await m_Redis.DisposeAsync();

	// **這一條進不了契約，而它是換 Redis 的全部理由。** 兩個 RedisChatRateLimiter 站在兩個
	// CommandRouter 複本的位置上：計數必須是共用的，否則上限就是「N 個複本 × 每秒 N 則」
	// （chat-layer.md 6.9 的第一個缺陷）。in-memory 的替身是兩個字典，這條在那邊必定紅。
	[Fact]
	public async Task TwoInstances_ShareTheCount_LikeTwoReplicasDo()
	{
		var clock = NewClock();
		var limit = new ChatRateLimit(MessagesPerSecond: 2);
		var replicaA = new RedisChatRateLimiter(m_Redis, clock, limit);
		var replicaB = new RedisChatRateLimiter(m_Redis, clock, limit);

		Assert.True(await replicaA.TryAcquireAsync("alice"));
		Assert.True(await replicaB.TryAcquireAsync("alice"));

		// 第三則不管打到哪一個複本都該被擋掉。
		Assert.False(await replicaA.TryAcquireAsync("alice"));
		Assert.False(await replicaB.TryAcquireAsync("alice"));
	}

	// **第二個缺陷：key 單調成長。** in-memory 那份字典沒有淘汰，鍵會隨「曾經發過訊息的使用者」
	// 一直長；Redis 版靠 TTL 解決，而那個 TTL 是 Lua script 裡 `count == 1` 那一支負責設的——
	// **拿掉它所有其他測試照過**，包括上面那條跨複本的。只有這一條會紅。
	[Fact]
	public async Task TheCounter_Expires_SoTheKeyspaceDoesNotGrow()
	{
		var clock = NewClock();
		var limiter = new RedisChatRateLimiter(m_Redis, clock, new ChatRateLimit(MessagesPerSecond: 1));

		Assert.True(await limiter.TryAcquireAsync("alice"));

		// key 的組成跟 RedisChatRateLimiter.Key 綁在一起（那是 internal 的實作細節，改了這裡要一起
		// 改）——跟 SnapshotConnectionsAsync 對 `Conn:` 前綴的處境相同，換來的是 TTL 可以被精確
		// 斷言，而不是靠「等兩秒看它有沒有消失」那種會讓測試變慢又變不穩的做法。
		var ttl = await m_Redis
			.GetDatabase()
			.KeyTimeToLiveAsync($"Chat:rate:alice:{clock.GetUtcNow().ToUnixTimeSeconds()}");

		Assert.NotNull(ttl);
		Assert.InRange(ttl.Value, TimeSpan.Zero, TimeSpan.FromSeconds(2));
	}

	protected override ValueTask<(IChatRateLimiter Limiter, AdvanceableClock Clock)> NewAsync(ChatRateLimit limit)
	{
		var clock = NewClock();

		return ValueTask.FromResult<(IChatRateLimiter, AdvanceableClock)>(
			(new RedisChatRateLimiter(m_Redis, clock, limit), clock));
	}

	protected override DateTimeOffset NewWindowStart() =>
		DefaultWindowStart.AddDays(Interlocked.Increment(ref s_Windows));
}
