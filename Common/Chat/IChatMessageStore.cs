namespace Common.Chat;

// **Append-only**：一次插入、一個 keyset 分頁查詢、一個批次刪除。沒有 read-modify-write，
// 不要求交易——所以任何 key-value 或文件儲存都給得起這三個方法（chat-layer.md 6.3）。
//
// 這句話在本文件初稿是假的：那一版要求交易內的 RMW（`UPDATE rooms SET last_seq ...`），
// 等於綁死了關聯式資料庫。改成應用發號（ADR-2）之後它才真的成立。
public interface IChatMessageStore
{
	// 訊息在進來之前就已經定序完成（OrderKey 由 IMessageSequencer 給），所以這裡沒有
	// read-modify-write。
	//
	// 主鍵衝突（同一房間、同一 order_key）回 false 而不是丟例外——呼叫端重新發號再試一次
	// 即可。因為寫入是單一插入、沒有交易，重試就是重發一次，沒有回滾要處理（6.4）。
	ValueTask<bool> TryAppendAsync(ChatMessage message, CancellationToken cancellationToken = default);

	// beforeOrderKey = 0 代表從最新的開始。回傳的訊息 order_key 由大到小。
	ValueTask<ChatMessagePage> GetPageAsync(
		string roomId,
		long beforeOrderKey,
		int limit,
		CancellationToken cancellationToken = default);

	// 回傳實際刪除的筆數，讓 sweeper 知道要不要再跑一輪（ADR-6）。
	//
	// 這是**保留期限**的清理路徑。房間被刪掉時的清理不走這裡，走 ON DELETE CASCADE
	// （ADR-10）——兩者規模差一到兩個數量級，混在一起會讓 sweeper 需要知道「房間存不存在」，
	// 那是它不該認識的事。
	ValueTask<int> DeleteOlderThanAsync(
		DateTimeOffset cutoff,
		int batchSize,
		CancellationToken cancellationToken = default);
}
