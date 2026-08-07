namespace Common.Chat;

// 階段 A 的實作。**訊息不持久化**——process 重啟就全部消失，所以這不是可以上線的東西；
// PostgreSQL 版本是階段 B（chat-layer.md ADR-4）。
//
// 存在的理由是把「行為」跟「儲存」分開驗：這一層的語意（先存後廣播、keyset 分頁、
// 排序鍵稀疏）在替身上就能釘死，換 store 實作時不必回頭改語意。這正是 append-only 帶來的
// 好處——初稿那版的「seq 連續無洞」是 Postgres 交易的性質，替身怎麼寫都會通過，等於在測
// 自己寫的替身（§9）。
//
// 用單一鎖而不是 per-room 鎖：這是替身，簡單比快重要。
internal sealed class InMemoryChatMessageStore : IChatMessageStore
{
	private readonly Lock m_Gate = new();

	// roomId -> order_key 遞增排序的訊息。SortedList 的鍵唯一，剛好就是
	// PRIMARY KEY (room_id, order_key) 那個守門。
	private readonly Dictionary<string, SortedList<long, ChatMessage>> m_Rooms = [];

	public ValueTask<bool> TryAppendAsync(ChatMessage message, CancellationToken cancellationToken = default)
	{
		lock (m_Gate)
		{
			if (!m_Rooms.TryGetValue(message.RoomId, out var byOrderKey))
				m_Rooms[message.RoomId] = byOrderKey = [];

			// 撞到就回 false，對應 Postgres 的 23505。呼叫端重新發號再試。
			if (byOrderKey.ContainsKey(message.OrderKey))
				return ValueTask.FromResult(false);

			byOrderKey.Add(message.OrderKey, message);

			return ValueTask.FromResult(true);
		}
	}

	public ValueTask<ChatMessagePage> GetPageAsync(
		string roomId,
		long beforeOrderKey,
		int limit,
		CancellationToken cancellationToken = default)
	{
		lock (m_Gate)
		{
			if (!m_Rooms.TryGetValue(roomId, out var byOrderKey))
				return ValueTask.FromResult(new ChatMessagePage([], false));

			// WHERE room_id = $1 AND order_key < $2 ORDER BY order_key DESC LIMIT $3 的等價物。
			// beforeOrderKey = 0 代表「從最新的開始」，所以沒有上界。
			var older = byOrderKey.Values
				.Where(message => beforeOrderKey == 0 || message.OrderKey < beforeOrderKey)
				.OrderByDescending(message => message.OrderKey)
				.ToList();

			// 多取一筆來判斷 has_more，而不是另外數一次總數——真的 SQL 也該這樣做。
			var page = older.Take(limit).ToList();

			return ValueTask.FromResult(new ChatMessagePage(page, older.Count > page.Count));
		}
	}

	public ValueTask<int> DeleteOlderThanAsync(
		DateTimeOffset cutoff,
		int batchSize,
		CancellationToken cancellationToken = default)
	{
		lock (m_Gate)
		{
			var deleted = 0;

			foreach (var byOrderKey in m_Rooms.Values)
			{
				foreach (var orderKey in byOrderKey
					.Where(entry => entry.Value.SentAt < cutoff)
					.Select(entry => entry.Key)
					.Take(batchSize - deleted)
					.ToList())
				{
					byOrderKey.Remove(orderKey);
					deleted++;
				}

				if (deleted >= batchSize)
					break;
			}

			return ValueTask.FromResult(deleted);
		}
	}
}
