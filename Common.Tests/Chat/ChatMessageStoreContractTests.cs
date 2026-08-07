using Common.Chat;

namespace Common.Tests.Chat;

// **這些是 IChatMessageStore 的契約，不是替身的規格。** 階段 B 的 PostgresChatMessageStore
// 必須通過同一組斷言——屆時把 NewStore() 換掉就行。
//
// 這麼寫是 append-only 帶來的好處：初稿那版的核心性質（seq 連續無洞、同房序列化）是
// Postgres 交易的性質，替身怎麼寫都會通過，等於在測自己寫的替身（chat-layer.md §9）。
// 現在要驗的是 keyset 分頁與範圍刪除，兩者在任何儲存上都是同一個語意。
public class ChatMessageStoreContractTests
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task TryAppend_ReturnsFalse_OnADuplicateOrderKeyInTheSameRoom()
	{
		var store = NewStore();

		Assert.True(await store.TryAppendAsync(Message("room-1", 100)));

		// 對應 Postgres 的 23505。呼叫端重新發號再試，不需要處理回滾（6.4）。
		Assert.False(await store.TryAppendAsync(Message("room-1", 100)));
	}

	[Fact]
	public async Task TryAppend_AllowsTheSameOrderKey_InDifferentRooms()
	{
		var store = NewStore();

		// 主鍵是 (room_id, order_key)，不是 order_key 單獨。跨程序碰撞只在同一間房才算衝突。
		Assert.True(await store.TryAppendAsync(Message("room-1", 100)));
		Assert.True(await store.TryAppendAsync(Message("room-2", 100)));
	}

	[Fact]
	public async Task GetPage_FromZero_ReturnsTheNewestFirst()
	{
		var store = await NewStoreWithAsync("room-1", 10, 20, 30);

		var page = await store.GetPageAsync("room-1", 0, 10);

		// before_order_key = 0 代表「從最新的開始」，回傳由大到小（5.2）。
		Assert.Equal([30, 20, 10], page.Messages.Select(message => message.OrderKey));
		Assert.False(page.HasMore);
	}

	[Fact]
	public async Task GetPage_IsExclusiveOnTheCursor_SoPagesDoNotOverlap()
	{
		var store = await NewStoreWithAsync("room-1", 10, 20, 30, 40);

		var first = await store.GetPageAsync("room-1", 0, 2);

		Assert.Equal([40, 30], first.Messages.Select(message => message.OrderKey));
		Assert.True(first.HasMore);

		// client 拿上一頁最小的 order_key 當下一次的游標。游標是**嚴格小於**，所以兩頁不會
		// 重疊也不會漏——這就是 keyset 分頁，翻到第 100 頁跟第 1 頁一樣快。
		var second = await store.GetPageAsync("room-1", first.Messages[^1].OrderKey, 2);

		Assert.Equal([20, 10], second.Messages.Select(message => message.OrderKey));
		Assert.False(second.HasMore);
	}

	[Fact]
	public async Task GetPage_BehavesTheSame_OnSparseOrderKeys()
	{
		// **排序鍵稀疏對分頁完全沒有影響**（ADR-2）：bigint 不管值多大都是 8 bytes，索引不在乎
		// 密度。稀疏唯一殺掉的是「對鍵做算術」，而那正是本層明確放棄的東西。
		var store = await NewStoreWithAsync("room-1", 1, 999_999, 1_700_000_000_000_001);

		var page = await store.GetPageAsync("room-1", 0, 2);

		Assert.Equal([1_700_000_000_000_001, 999_999], page.Messages.Select(message => message.OrderKey));
		Assert.True(page.HasMore);
	}

	[Fact]
	public async Task GetPage_IsScopedToOneRoom()
	{
		var store = NewStore();
		await store.TryAppendAsync(Message("room-1", 10));
		await store.TryAppendAsync(Message("room-2", 20));

		var page = await store.GetPageAsync("room-1", 0, 10);

		Assert.Equal([10], page.Messages.Select(message => message.OrderKey));
	}

	[Fact]
	public async Task GetPage_ReturnsEmpty_ForARoomWithNoMessages()
	{
		var page = await NewStore().GetPageAsync("nobody-spoke-here", 0, 10);

		Assert.Empty(page.Messages);
		Assert.False(page.HasMore);
	}

	[Fact]
	public async Task DeleteOlderThan_OnlyRemovesMessagesBeforeTheCutoff()
	{
		var store = NewStore();
		await store.TryAppendAsync(Message("room-1", 10, _Now.AddDays(-100)));
		await store.TryAppendAsync(Message("room-1", 20, _Now.AddDays(-10)));

		Assert.Equal(1, await store.DeleteOlderThanAsync(_Now.AddDays(-90), 5000));

		var page = await store.GetPageAsync("room-1", 0, 10);

		Assert.Equal([20], page.Messages.Select(message => message.OrderKey));
	}

	[Fact]
	public async Task DeleteOlderThan_HonoursTheBatchSize_AndReportsWhatItDeleted()
	{
		var store = NewStore();

		foreach (var orderKey in Enumerable.Range(1, 10))
			await store.TryAppendAsync(Message("room-1", orderKey, _Now.AddDays(-100)));

		// 回傳實際刪除的筆數，這正是 sweeper 判斷「要不要再跑一輪」的依據（ADR-6）。
		Assert.Equal(4, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(4, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(2, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(0, await store.DeleteOlderThanAsync(_Now, 4));
	}

	private static IChatMessageStore NewStore() => new InMemoryChatMessageStore();

	private static async Task<IChatMessageStore> NewStoreWithAsync(string roomId, params long[] orderKeys)
	{
		var store = NewStore();

		foreach (var orderKey in orderKeys)
			Assert.True(await store.TryAppendAsync(Message(roomId, orderKey)));

		return store;
	}

	private static ChatMessage Message(string roomId, long orderKey, DateTimeOffset? sentAt = null) =>
		new(roomId, orderKey, "alice", "Alice", "hi", sentAt ?? _Now);
}
