namespace Common.Chat;

// 每人每秒最多幾則。暫定值（chat-layer.md §9）。
public sealed record ChatRateLimit(int MessagesPerSecond)
{
	public static readonly ChatRateLimit Default = new(MessagesPerSecond: 10);
}

// 限流放在 handler 而**不是** IInboundFilter（chat-layer.md ADR-7）：`FilterDecision` 只有
// Allow/Drop/Terminate，沒有辦法回一則訊息給 client，而被限流的人收到 Drop 就是沉默——
// 那正是 protocol-layer.md ADR-8 要求業務失敗必須明確回覆所要避免的狀況。
//
// （那條 ADR 也是 IInboundFilter 最後一個候選使用者，它落在這裡之後整個機制就被移除了。）
//
// 觸發限流的不是吞吐量，是**持久儲存**：chat.send 是第一個會寫入無上限成長的儲存的命令，
// 房間層那 8 個命令全部是有界操作。
public interface IChatRateLimiter
{
	// true = 這一則可以送。
	ValueTask<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default);
}

// 階段 A 的實作。**跨複本不成立**——CommandRouter 是多複本，記憶體計數器只擋得住打到同一個
// 複本的請求，所以實際上限是 N 個複本 × 每秒 10 則。階段 B 換成 Redis
// （`INCR Chat:rate:{userId}:{unixSecond}` + `EXPIRE 2`，ADR-7）。
//
// 第二個「只能是暫時的」理由：這裡的字典**沒有淘汰**，鍵會隨著「曾經發過訊息的使用者」
// 單調成長。Redis 版本靠 TTL 自然解決。
internal sealed class InMemoryChatRateLimiter(TimeProvider timeProvider, ChatRateLimit limit) : IChatRateLimiter
{
	private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Window> m_Windows = new();

	public ValueTask<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
	{
		var second = timeProvider.GetUtcNow().ToUnixTimeSeconds();
		var window = m_Windows.GetOrAdd(userId, _ => new Window());

		// 刻意用鎖而不是 ConcurrentDictionary.AddOrUpdate：後者的 update factory 可能被呼叫
		// 多次，計數會在競爭下遺失——而「同一個使用者同時猛送」正是這個限流器要擋的情況，
		// 在那個情況下少算等於沒擋。
		lock (window)
		{
			if (window.Second != second)
			{
				window.Second = second;
				window.Count = 0;
			}

			return ValueTask.FromResult(++window.Count <= limit.MessagesPerSecond);
		}
	}

	private sealed class Window
	{
		public long Second;

		public int Count;
	}
}
