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
//
// 正式實作是 `RedisChatRateLimiter`——計數必須跨 CommandRouter 複本，這是這個介面存在的**全部**
// 理由（6.9）。階段 A 的 `InMemoryChatRateLimiter` 曾經住在這個檔案裡，現在是
// `Common.Tests.Chat` 的測試替身：跟 `InMemoryChatMessageStore` 一樣，正式路徑上不該留著一個
// 「N 個複本就是 N 倍上限」的限流器讓人選錯。
public interface IChatRateLimiter
{
	// true = 這一則可以送。
	ValueTask<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default);
}
