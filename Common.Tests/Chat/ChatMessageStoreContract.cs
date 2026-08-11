using Common.Chat;
using Common.Rooms;

namespace Common.Tests.Chat;

// **這些是 IChatMessageStore 的契約，不是替身的規格。** 抽象基底，每個實作派生一個殼去跑同一組
// 斷言：in-memory 在 Common.Tests（0.4 秒、不需要容器），PostgreSQL 在 E2E.Tests。**B1 之前這
// 一份是具體類別**，改成基底就是它當初被寫下來的目的。
//
// 這麼寫是 append-only 帶來的好處：初稿那版的核心性質（seq 連續無洞、同房序列化）是
// Postgres 交易的性質，替身怎麼寫都會通過，等於在測自己寫的替身（chat-layer.md §9）。
// 現在要驗的是 keyset 分頁與範圍刪除，兩者在任何儲存上都是同一個語意。
public abstract class ChatMessageStoreContract
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task TryAppend_ReturnsFalse_OnADuplicateOrderKeyInTheSameRoom()
	{
		var store = await NewStoreWithRoomsAsync("room-1");

		Assert.True(await store.TryAppendAsync(Message("room-1", 100)));

		// **這一條在 Postgres 派生上才真的有在驗東西**（§9 列的兩件「需要真資料庫」之一）：
		// 替身是字典的 ContainsKey，Postgres 是 ON CONFLICT DO NOTHING 影響 0 列。
		Assert.False(await store.TryAppendAsync(Message("room-1", 100)));
	}

	[Fact]
	public async Task TryAppend_AllowsTheSameOrderKey_InDifferentRooms()
	{
		var store = await NewStoreWithRoomsAsync("room-1", "room-2");

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
	public async Task GetPage_PreservesTheMessage_FieldForField()
	{
		// **B1 補的一條。** 先前每條斷言都只看 OrderKey，所以「欄位對映錯了」——欄位順序寫反、
		// timestamptz 掉了時區——在這一組裡完全隱形。房間層 B2 踩過同一個坑，而那次的症狀是
		// `room.list.reply` 沒回來，離真正的原因非常遠（room-layer.md §9）。
		var store = await NewStoreWithRoomsAsync("room-1");
		var sent = new ChatMessage("room-1", 42, "alice", "Alice Liddell", "hi there", _Now);

		Assert.True(await store.TryAppendAsync(sent));

		var page = await store.GetPageAsync("room-1", 0, 10);

		Assert.Equal(sent, Assert.Single(page.Messages));
	}

	[Fact]
	public async Task GetPage_IsScopedToOneRoom()
	{
		var store = await NewStoreWithRoomsAsync("room-1", "room-2");

		await store.TryAppendAsync(Message("room-1", 10));
		await store.TryAppendAsync(Message("room-2", 20));

		var page = await store.GetPageAsync("room-1", 0, 10);

		Assert.Equal([10], page.Messages.Select(message => message.OrderKey));
	}

	[Fact]
	public async Task GetPage_ReturnsEmpty_ForARoomWithNoMessages()
	{
		var store = await NewStoreWithRoomsAsync("nobody-spoke-here");

		var page = await store.GetPageAsync("nobody-spoke-here", 0, 10);

		Assert.Empty(page.Messages);
		Assert.False(page.HasMore);
	}

	[Fact]
	public async Task DeleteOlderThan_OnlyRemovesMessagesBeforeTheCutoff()
	{
		var store = await NewStoreWithRoomsAsync("room-1");

		await store.TryAppendAsync(Message("room-1", 10, _Now.AddDays(-100)));
		await store.TryAppendAsync(Message("room-1", 20, _Now.AddDays(-10)));

		Assert.Equal(1, await store.DeleteOlderThanAsync(_Now.AddDays(-90), 5000));

		var page = await store.GetPageAsync("room-1", 0, 10);

		Assert.Equal([20], page.Messages.Select(message => message.OrderKey));
	}

	[Fact]
	public async Task DeleteOlderThan_HonoursTheBatchSize_AndReportsWhatItDeleted()
	{
		var store = await NewStoreWithRoomsAsync("room-1");

		foreach (var orderKey in Enumerable.Range(1, 10))
			await store.TryAppendAsync(Message("room-1", orderKey, _Now.AddDays(-100)));

		// 回傳實際刪除的筆數，這正是 sweeper 判斷「要不要再跑一輪」的依據（ADR-6）。
		Assert.Equal(4, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(4, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(2, await store.DeleteOlderThanAsync(_Now, 4));
		Assert.Equal(0, await store.DeleteOlderThanAsync(_Now, 4));
	}

	// 實作提供一組**空的** store + 房間 store。兩個一起給的理由跟 RoomBanListContract 一模一樣：
	// `messages.room_id` 有 FK 指向 `rooms`（ADR-10 的 CASCADE 機制），所以「房間必須存在」是這個
	// 介面沒被寫下來的前置條件，而建房間只能經由 IRoomStore。
	//
	// **那條前置條件一直都成立**——ChatSendHandler 送訊息之前先查成員資格，而成員資格的前提是
	// 房間存在。跟房間層 B2 那次一樣，是換儲存才被 FK 逼出來的。
	protected abstract ValueTask<(IChatMessageStore Messages, IRoomStore Rooms)> NewAsync();

	private async Task<IChatMessageStore> NewStoreWithRoomsAsync(params string[] roomIds)
	{
		var (messages, rooms) = await NewAsync();

		foreach (var roomId in roomIds)
			Assert.True(await rooms.TryCreateAsync(new Room(roomId, "Lobby", null, "owner", _Now)));

		return messages;
	}

	private async Task<IChatMessageStore> NewStoreWithAsync(string roomId, params long[] orderKeys)
	{
		var store = await NewStoreWithRoomsAsync(roomId);

		foreach (var orderKey in orderKeys)
			Assert.True(await store.TryAppendAsync(Message(roomId, orderKey)));

		return store;
	}

	private static ChatMessage Message(string roomId, long orderKey, DateTimeOffset? sentAt = null) =>
		new(roomId, orderKey, "alice", "Alice", "hi", sentAt ?? _Now);
}
