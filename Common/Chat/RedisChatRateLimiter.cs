using StackExchange.Redis;

namespace Common.Chat;

// ADR-7 的限流：`INCR Chat:rate:{userId}:{unixSecond}`，key 自己過期。
//
// **固定視窗，不是滑動視窗。** 邊界上最壞情況是 2 倍上限（前一秒的最後一刻與這一秒的第一刻各
// 送滿），那對「防止有人灌爆持久儲存」這個目的來說完全足夠——ADR-7 要的是一個量級上的閘門，
// 不是精確的節流。滑動視窗要存時間戳集合（ZSET + ZREMRANGEBYSCORE），每則訊息的成本從一個
// INCR 變成一組區間操作，換到的精度在這個目的下買不到東西。
internal sealed class RedisChatRateLimiter(
	IConnectionMultiplexer multiplexer,
	TimeProvider timeProvider,
	ChatRateLimit limit) : IChatRateLimiter
{
	// **TTL 兩秒而不是一秒**，理由是複本之間的時鐘偏差：second 是由呼叫端算的（見下面 Key），
	// 所以一個稍微落後的複本可能在真實時間已經進入 X+1 之後，還在寫 X 這個 key。TTL 只有一秒
	// 時那個 key 可能已經被回收，於是它的計數從 1 重新開始——**放寬限制而且靜默**。
	private const int WindowTtlSeconds = 2;

	// **一次往返，而且 INCR 與 EXPIRE 不會被拆開。** ADR-7 給限流的預算是「每則訊息多一次 Redis
	// 往返」，兩個獨立的 await 會用掉兩次；更重要的是那樣 INCR 成功、EXPIRE 沒送出時會留下一個
	// **永遠不過期**的 key，而「key 隨著曾經發言的使用者單調成長」正是階段 A 那個替身唯一靠換
	// Redis 才能解的缺陷之一（6.9）。用 Lua 就不會有那個中間狀態。
	//
	// 只在 count == 1 時 EXPIRE：key 的壽命跟它代表的那一秒綁定，續期沒有意義，省一個命令。
	//
	// 只碰單一 key，所以在 Redis Cluster 上不會有跨 slot 的問題（跟 RedisPresenceDirectory
	// 那個 script 同一個約束）。
	private const string IncrementScript =
		"""
		local count = redis.call('INCR', KEYS[1])
		if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
		return count
		""";

	public async ValueTask<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
	{
		// **超過上限之後仍然繼續 INCR**，跟 in-memory 的替身一樣：被擋掉不會換來一份新的額度，
		// 而 key 本來就會過期，所以「一直送」不會把計數養大到需要處理的程度。
		var count = (long)await multiplexer.GetDatabase()
			.ScriptEvaluateAsync(
				IncrementScript,
				[Key(userId, timeProvider.GetUtcNow().ToUnixTimeSeconds())],
				[WindowTtlSeconds])
			.ConfigureAwait(false);

		// Redis 連不上時例外往上丟，跟這個 codebase 裡每一個 Redis 儲存一致（fail-closed：
		// 送不出去比放行安全）。**這不是新增的故障點**——同一條路徑在到限流之前已經打過 Redis
		// 一次（`GetCurrentRoomAsync` 讀成員名單），Redis 掛掉的時候根本走不到這裡。
		return count <= limit.MessagesPerSecond;
	}

	// **second 由呼叫端的時鐘算，不是 Redis 的 TIME。** 用 `redis.call('TIME')` 可以讓所有複本
	// 對齊同一個時鐘，但 key 就得在 script 裡面拼出來——而 Redis Cluster 靠 KEYS 決定 slot，
	// script 裡憑空長出來的 key 是不合法的。代價寫在 WindowTtlSeconds 上面。
	private static string Key(string userId, long unixSecond) => $"Chat:rate:{userId}:{unixSecond}";
}
