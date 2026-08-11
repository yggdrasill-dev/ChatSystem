using Common.Chat;

namespace Common.Tests.Chat;

// IChatMessageStore 的 in-memory 實作。**階段 A 它是真的註冊進 DI 的正式實作**（住在
// Common/Chat/），B1 換成 PostgresChatMessageStore 之後降級成測試替身，所以跟房間層 B2 一樣搬進
// 測試專案——正式路徑上不該留著一個「重啟就消失」的儲存讓人選錯。
//
// 它同時服務兩個地方：ChatMessageStoreContract 拿它當契約的 in-memory 跑道（另一個跑道是
// E2E.Tests 的 Postgres 派生），Integration.Tests 拿它讓層與層的組合不需要容器。**只留一份**是
// 刻意的，理由跟 InMemoryRoomStores.cs 那句一樣。
//
// 用單一鎖而不是 per-room 鎖：這是替身，簡單比快重要。
public sealed class InMemoryChatMessageStore : IChatMessageStore
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

			// 撞到就回 false。Postgres 版是 ON CONFLICT DO NOTHING 的 0 列，兩邊對呼叫端一樣：
			// 重新發號再試。
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

			// 多取一筆來判斷 has_more，而不是另外數一次總數——真的 SQL 也該這樣做，而它現在
			// 真的是那樣做的（PostgresChatMessageStore.GetPageAsync 的 probe）。
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
