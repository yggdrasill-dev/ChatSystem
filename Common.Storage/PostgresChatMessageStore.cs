using Common.Storage;
using Microsoft.EntityFrameworkCore;

namespace Common.Chat;

// chat-layer.md ADR-4 的正式儲存，形狀比照 PostgresRoomStore：**每個方法都是單一語句**，沒有
// read-modify-write、不需要交易。這裡是 append-only（ADR-2）換來的，不是巧合——初稿那版要求
// 交易內更新 `rooms.last_seq`，那個設計下這個類別會長得完全不同，而且**會是 EF Core 唯一真正
// 有價值的地方**（變更追蹤 + 交易）。現在那個價值不存在。
//
// 拿 IDbContextFactory 而不是 ChatDbContext 的理由見 PostgresRoomStore：這個 store 是 singleton，
// 而 ChatRetentionSweeper 那個 BackgroundService 也持有它。
internal sealed class PostgresChatMessageStore(IDbContextFactory<ChatDbContext> dbFactory) : IChatMessageStore
{
	public async ValueTask<bool> TryAppendAsync(ChatMessage message, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **`ON CONFLICT DO NOTHING`，不是 `db.Messages.Add()` + `SaveChangesAsync()`。** 文件 6.5
		// 原本寫「撞到就是 23505」，而這個寫法讓那個錯誤碼根本不會產生：守門的表現形式是「影響
		// 0 列」。對呼叫端完全一樣（回 false、重新發號再試），但這是**每則訊息都會走的熱路徑**，
		// 把控制流建在例外上要付的不只是效能，還有「從 DbUpdateException 拆出 SqlState 才分得出
		// 撞主鍵和連線斷了」這種診斷成本。
		//
		// **房間不存在時的 23503 刻意不接**：那是 room_id 的 FK（ADR-10 的 CASCADE 機制）。
		// 把它也翻譯成 false 會讓 ChatSendHandler 重試三次，然後丟一個說「連續撞到同一個
		// order_key」的例外——訊息是錯的，而錯的診斷比沒有診斷更貴。讓它原樣往上丟，協定層記
		// error 並回 HANDLER_FAILED。這條路徑是 ADR-8 明確接受的毫秒級 TOCTOU：送訊息的授權查
		// 的是 Redis 的成員名單，房間在那之後被刪掉就會走到這裡。
		var inserted = await db.Database
			.ExecuteSqlAsync(
				$"""
				INSERT INTO messages (room_id, order_key, sender_user_id, sender_display_name, body, sent_at)
				VALUES ({message.RoomId}, {message.OrderKey}, {message.SenderUserId},
					{message.SenderDisplayName}, {message.Body}, {message.SentAt})
				ON CONFLICT (room_id, order_key) DO NOTHING
				""",
				cancellationToken)
			.ConfigureAwait(false);

		return inserted == 1;
	}

	public async ValueTask<ChatMessagePage> GetPageAsync(
		string roomId,
		long beforeOrderKey,
		int limit,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **「從最新的開始」在 C# 這一側翻譯成一個上界**，而不是把 `beforeOrderKey == 0` 寫進
		// Where 裡變成一個 OR。order_key 是微秒時間戳，long.MaxValue 是它到不了的上界。
		//
		// **這裡原本寫著「OR 會讓 planner 放棄 index scan 改成 seq scan」，那句話在 EF Core 之下
		// 是錯的，實測過**（`ChatMessageQueryPlanTests` 加進來時順手驗的）：EF 在翻譯階段就知道
		// 參數的值，`beforeOrderKey == 0` 為真時整條 OR 被折疊掉，送到 Postgres 的 SQL 連
		// `order_key` 的條件都沒有；為假時折成單純的 `order_key < @before`。**兩種值各產生一份
		// SQL，計畫都是 PK 的 index scan。** 那句警告是手寫 SQL（Dapper）時代的，那時 OR 會原樣
		// 送出去。
		//
		// 所以這一行留著的理由變成比較平淡的兩個：讀的人不必知道 EF 的優化器做了什麼，以及
		// 「兩種輸入產生兩份 SQL」這件事不必發生。**它不再是在防一個已知的效能陷阱。**
		var cursor = beforeOrderKey == 0 ? long.MaxValue : beforeOrderKey;

		// 多取一筆來判斷 has_more，而不是另外 COUNT 一次：後者要再掃一次同一段範圍。
		var rows = await db.Messages
			.AsNoTracking()
			.Where(message => message.RoomId == roomId && message.OrderKey < cursor)
			.OrderByDescending(message => message.OrderKey)
			.Take(limit + 1)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		var hasMore = rows.Count > limit;

		if (hasMore)
			rows.RemoveAt(rows.Count - 1);

		return new ChatMessagePage(rows, hasMore);
	}

	public async ValueTask<int> DeleteOlderThanAsync(
		DateTimeOffset cutoff,
		int batchSize,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **這裡是 raw SQL，因為 `ExecuteDeleteAsync` 不支援 `Take()`**（EF 會丟「不能在 delete 上
		// 用 Take」）。繞法是 `Where(m => subquery.Contains(m))`，但複合主鍵的 Contains 翻譯出來
		// 是什麼形狀得看產生的 SQL 才知道——為了一個批次上限去賭 planner，不如直接寫清楚。
		//
		// 分批的理由在 ChatRetentionSweeper：一次刪掉 90 天前的全部會變成長交易。
		//
		// **用 `ctid` 是安全的**，即使它是物理位址：子查詢與 DELETE 在同一個語句、同一個 snapshot
		// 裡，而這張表是 append-only（沒有任何 UPDATE 會讓列搬家）。
		//
		// 多個複本各跑一個 sweeper 時，兩邊會挑到重疊的列——後到的那個等前一個的鎖、然後刪到 0 列。
		// 結果仍然正確（sweeper 本來就要求 idempotent），代價只是白做工，所以這裡不加
		// `FOR UPDATE SKIP LOCKED`。
		return await db.Database
			.ExecuteSqlAsync(
				$"""
				DELETE FROM messages
				WHERE ctid IN (
					SELECT ctid FROM messages WHERE sent_at < {cutoff} LIMIT {batchSize}
				)
				""",
				cancellationToken)
			.ConfigureAwait(false);
	}
}
