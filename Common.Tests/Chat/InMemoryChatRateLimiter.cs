using Common.Chat;

namespace Common.Tests.Chat;

// IChatRateLimiter 的 in-memory 實作。**階段 A 它是真的註冊進 DI 的正式實作**（住在
// Common/Chat/IChatRateLimiter.cs 裡），階段 B 換成 RedisChatRateLimiter 之後降級成測試替身，
// 跟 InMemoryChatMessageStore 走同一條路——正式路徑上不該留著一個「N 個複本就是 N 倍上限」的
// 限流器讓人選錯。
//
// 它服務兩個地方：ChatRateLimiterContract 的 in-memory 跑道（另一個跑道是 E2E.Tests 的真 Redis），
// 以及 ChatHandlerTests / Integration.Tests 讓 chat.send 的測試不需要容器。
//
// **兩個已知缺陷都留著，而且是刻意的**（chat-layer.md 6.9）：計數不跨程序、字典沒有淘汰。它們
// 正是這個實作只能當替身的理由，補起來只會讓它看起來像可以用。
public sealed class InMemoryChatRateLimiter(TimeProvider timeProvider, ChatRateLimit limit) : IChatRateLimiter
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
