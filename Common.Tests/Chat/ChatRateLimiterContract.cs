using Common.Chat;

namespace Common.Tests.Chat;

// **這些是 IChatRateLimiter 的契約，不是替身的規格。** 抽象基底，每個實作派生一個殼去跑同一組
// 斷言：in-memory 在 Common.Tests（不需要容器），Redis 在 E2E.Tests——跟 ChatMessageStoreContract
// 同一個形狀，理由也同一個：**換掉實作時該有東西接住**。
//
// 這一份是先寫契約再寫 Redis 實作，跟房間層 B0 那 20 條一樣。房間層那次的教訓是反過來的：原本
// 只有對著 mock IDatabase 的白箱測試，換掉實作就整份作廢（room-layer.md §9）。所以這裡**沒有**
// 對著 mock IDatabase 驗「有送出 INCR」的測試——那種測試只能證明「我送了我打算送的東西」，
// 而這個實作真正會錯的地方在 Lua 的行為與 key 的組成，兩者都只有真 Redis 答得出來。
//
// **時間由測試控制**：兩個實作的視窗都是 `unixSecond`、都由呼叫端的時鐘算出來（Redis 版刻意不用
// redis 的 TIME，理由見 RedisChatRateLimiter.Key），所以推一個假時鐘就能讓「視窗換了」在兩邊
// 都是同一件事，而且不必真的等一秒。
public abstract class ChatRateLimiterContract
{
	protected static readonly DateTimeOffset DefaultWindowStart =
		DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task Allows_EveryMessage_UpToTheLimit()
	{
		var (limiter, _) = await NewAsync(new ChatRateLimit(MessagesPerSecond: 3));

		Assert.True(await limiter.TryAcquireAsync("alice"));
		Assert.True(await limiter.TryAcquireAsync("alice"));
		Assert.True(await limiter.TryAcquireAsync("alice"));
	}

	[Fact]
	public async Task Blocks_TheMessageAfterTheLimit()
	{
		var (limiter, _) = await NewAsync(new ChatRateLimit(MessagesPerSecond: 3));

		for (var i = 0; i < 3; i++)
			await limiter.TryAcquireAsync("alice");

		Assert.False(await limiter.TryAcquireAsync("alice"));
	}

	[Fact]
	public async Task KeepsBlocking_WithinTheSameWindow()
	{
		var (limiter, _) = await NewAsync(new ChatRateLimit(MessagesPerSecond: 1));

		Assert.True(await limiter.TryAcquireAsync("alice"));

		// 被擋掉不會換來一份新的額度——實作若在拒絕時順手把計數歸零，第二次就會變成 true。
		Assert.False(await limiter.TryAcquireAsync("alice"));
		Assert.False(await limiter.TryAcquireAsync("alice"));
	}

	[Fact]
	public async Task Resets_WhenTheWindowRolls()
	{
		var (limiter, clock) = await NewAsync(new ChatRateLimit(MessagesPerSecond: 1));

		Assert.True(await limiter.TryAcquireAsync("alice"));
		Assert.False(await limiter.TryAcquireAsync("alice"));

		clock.Advance(TimeSpan.FromSeconds(1));

		// 固定視窗（RedisChatRateLimiter 的開頭那段）：跨過秒的邊界就是全新的額度。
		Assert.True(await limiter.TryAcquireAsync("alice"));
	}

	[Fact]
	public async Task Counts_EachUserSeparately()
	{
		var (limiter, _) = await NewAsync(new ChatRateLimit(MessagesPerSecond: 1));

		Assert.True(await limiter.TryAcquireAsync("alice"));
		Assert.False(await limiter.TryAcquireAsync("alice"));

		// 一個人把自己的額度用完，不該影響別人——這是「每人每秒」而不是「每秒」。
		Assert.True(await limiter.TryAcquireAsync("bob"));
	}

	// 派生類別建一個限流器，並拿到測試要推的那個時鐘。
	protected abstract ValueTask<(IChatRateLimiter Limiter, AdvanceableClock Clock)> NewAsync(ChatRateLimit limit);

	// 時鐘的起點。**Redis 派生會覆寫它**：那個實作的 key 帶著 unixSecond，而它跑在一顆跟整組 E2E
	// 共用的真 Redis 上——每條測試落在不同的秒，就等於落在不同的 key，不必也不能去清 keyspace
	// （FLUSHDB 會連正在跑的 app 的 session 一起清掉）。
	protected virtual DateTimeOffset NewWindowStart() => DefaultWindowStart;

	// 起點由 NewWindowStart() 決定，所以工廠方法自己不必操心。
	protected AdvanceableClock NewClock() => new(NewWindowStart());
}

// 契約唯一需要的時鐘能力：往前推。刻意跟 MonotonicMicrosecondSequencerTests 那個 Set 語意的
// 時鐘分開——那一份要能把時鐘往**回**推（NTP 那一條），這裡不需要。
public sealed class AdvanceableClock(DateTimeOffset start) : TimeProvider
{
	private DateTimeOffset m_Now = start;

	public override DateTimeOffset GetUtcNow() => m_Now;

	public void Advance(TimeSpan amount) => m_Now += amount;
}
