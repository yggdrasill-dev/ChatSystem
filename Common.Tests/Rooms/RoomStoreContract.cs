using Common.Rooms;

namespace Common.Tests.Rooms;

// **這些是 IRoomStore 的契約，不是替身的規格。** 抽象基底，每個實作派生一個殼去跑同一組斷言：
// `InMemoryRoomStoreContractTests`（這個專案）與 `PostgresRoomStoreContractTests`（E2E.Tests，
// 因為它需要真的資料庫）。B0 寫這一份的時候只有 in-memory，B2 讓 Postgres 也接了上來。
//
// 房間層原本沒有這個東西，這是 RedisRoomStoreTests 蓋不到的那一半：那些斷言的是 Redis
// 指令的形狀（SADD 哪個 key、HashSetAsync 哪些欄位），換掉實作整份作廢、一條都接不過去。
// 這裡只寫「任何儲存都必須成立」的語意，所以兩份是互補而不是重複。
//
// 一條刻意**不在**這裡的保證：刪房會把封鎖名單一起帶走。它在每個實作上長得不一樣
// （Postgres 是 ON DELETE CASCADE、Redis 是 TryDeleteAsync 裡多刪一個 key），而 in-memory
// 的兩個替身是獨立物件、湊不出那個連動。由各實作自己的測試守。
public abstract class RoomStoreContract
{
	// 固定值而不是 UtcNow：Redis 版把 CreatedAt 存成 unix 毫秒，UtcNow 帶的次毫秒 ticks
	// round-trip 之後會被截掉。**契約的精度上限就是毫秒**，Postgres 的 timestamptz 更精確
	// 也不改變這一點——這是介面允許的最小保證，不是實作的能力。
	private static readonly DateTimeOffset _CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task Get_ReturnsNull_WhenTheRoomDoesNotExist() =>
		Assert.Null(await (await NewStoreAsync()).GetAsync("never-created"));

	[Fact]
	public async Task TryCreate_ThenGet_RoundTripsEveryField()
	{
		var store = await NewStoreWithAsync(NewRoom(passwordHash: "hash-1"));

		var room = await store.GetAsync("room-1");

		Assert.NotNull(room);
		Assert.Equal("room-1", room.RoomId);
		Assert.Equal("Lobby", room.Name);
		Assert.Equal("hash-1", room.PasswordHash);
		Assert.Equal("user-1", room.OwnerUserId);
		Assert.Equal(_CreatedAt, room.CreatedAt);
	}

	[Fact]
	public async Task Get_ReturnsANullPassword_ForAPublicRoom()
	{
		var store = await NewStoreWithAsync(NewRoom(passwordHash: null));

		// **null 不是空字串。** 兩種儲存都有一個很好走的錯路：Redis 的 hash 欄位沒有 null，
		// 存進去的是空字串；Postgres 允許 NULL 但 INSERT 很容易寫成 COALESCE(..., '')。
		// 而 room.list 的 has_password 直接看這個欄位，映射錯了就是「公開房顯示成密碼房」。
		Assert.Null((await store.GetAsync("room-1"))!.PasswordHash);
	}

	[Fact]
	public async Task TryCreate_ReturnsFalse_AndDoesNotOverwrite_WhenTheIdIsAlreadyTaken()
	{
		var store = await NewStoreWithAsync(NewRoom(ownerUserId: "user-1"));

		// 這是本介面唯一不能有競爭的操作（IRoomStore 的介面註解）。「回 false」跟「原本那間房
		// 沒被動到」是兩件事，後者才是 lost update 真正的樣子。
		Assert.False(await store.TryCreateAsync(NewRoom(ownerUserId: "user-2")));
		Assert.Equal("user-1", (await store.GetAsync("room-1"))!.OwnerUserId);
	}

	[Fact]
	public async Task List_ReturnsEmpty_WhenThereAreNoRooms() =>
		Assert.Empty(await (await NewStoreAsync()).ListAsync());

	[Fact]
	public async Task List_ReturnsEveryRoom()
	{
		var store = await NewStoreWithAsync(NewRoom("room-1"), NewRoom("room-2"));

		// **順序不在契約裡**，所以排序過再比：Redis 走 SMEMBERS（集合無序），Postgres 沒有
		// ORDER BY 的 SELECT 同樣不保證順序。要是哪天 room.list 需要固定順序，那是新增一條
		// 契約而不是「本來就有」。
		Assert.Equal(["room-1", "room-2"], (await store.ListAsync()).Select(room => room.RoomId).Order());
	}

	[Fact]
	public async Task List_ExcludesDeletedRooms()
	{
		var store = await NewStoreWithAsync(NewRoom("room-1"), NewRoom("room-2"));

		Assert.True(await store.TryDeleteAsync("room-2"));

		// 產品面的意思是「關掉的房間不會留在大廳」。ADR-10 之前這條靠 ListOpenAsync 過濾
		// IsClosed，現在靠「那一列真的不在了」。
		Assert.Equal(["room-1"], (await store.ListAsync()).Select(room => room.RoomId));
	}

	[Fact]
	public async Task TryUpdateSettings_ChangesTheNameAndPassword_AndNothingElse()
	{
		var store = await NewStoreWithAsync(NewRoom(passwordHash: "hash-1"));

		Assert.True(await store.TryUpdateSettingsAsync("room-1", "Lounge", "hash-2"));

		var room = await store.GetAsync("room-1");

		Assert.Equal("Lounge", room!.Name);
		Assert.Equal("hash-2", room.PasswordHash);

		// 房主與建立時間**不可變更**（Room 的註解說明了為什麼：後台三個流程都靠「房主永遠不會變」
		// 才對 TOCTOU 安全）。Postgres 版寫成整列 UPDATE 就會靜默洗掉它們。
		Assert.Equal("user-1", room.OwnerUserId);
		Assert.Equal(_CreatedAt, room.CreatedAt);
	}

	[Fact]
	public async Task TryUpdateSettings_CanClearThePassword()
	{
		var store = await NewStoreWithAsync(NewRoom(passwordHash: "hash-1"));

		Assert.True(await store.TryUpdateSettingsAsync("room-1", "Lobby", null));

		// 密碼房改回公開房，讀回來必須是 null——跟上面那條公開房是同一個映射，但這條走的是
		// UPDATE 而不是 INSERT，兩條路徑會分開寫、也會分開錯。E2E 有一條「改設定清掉密碼」。
		Assert.Null((await store.GetAsync("room-1"))!.PasswordHash);
	}

	[Fact]
	public async Task TryUpdateSettings_ReturnsFalse_WhenTheRoomDoesNotExist() =>
		Assert.False(await (await NewStoreAsync()).TryUpdateSettingsAsync("never-created", "Lounge", null));

	[Fact]
	public async Task TryUpdateSettings_DoesNotResurrectADeletedRoom()
	{
		var store = await NewStoreWithAsync(NewRoom());

		Assert.True(await store.TryDeleteAsync("room-1"));

		// **ADR-10 帶進來的新契約。** 在那之前，update 撞上 close 只是「改到一間已關閉的房間」，
		// 6.1 把它列為刻意接受的無害競爭。真刪之後同一個競爭會變成「把刪掉的房間寫回來」——
		// 而寫回來的那一份只有 name 與 password_hash，沒有房主。**回 false 而不是重建**，
		// 兩種儲存都要自己保證：Postgres 靠 UPDATE 影響 0 列，Redis 靠一段 EXISTS 守門的 Lua。
		Assert.False(await store.TryUpdateSettingsAsync("room-1", "Lounge", null));
		Assert.Null(await store.GetAsync("room-1"));
	}

	[Fact]
	public async Task TryDelete_RemovesTheRoom()
	{
		var store = await NewStoreWithAsync(NewRoom());

		Assert.True(await store.TryDeleteAsync("room-1"));
		Assert.Null(await store.GetAsync("room-1"));
	}

	[Fact]
	public async Task TryDelete_ReturnsFalse_OnTheSecondCall()
	{
		var store = await NewStoreWithAsync(NewRoom());

		// 「只有一個呼叫端刪得到」是 RoomCloseHandler 用來決定要不要廣播 RoomClosed 的依據，
		// 所以兩個併發的 close 只會廣播一次。ADR-10 之前這裡是刻意接受的重複廣播。
		Assert.True(await store.TryDeleteAsync("room-1"));
		Assert.False(await store.TryDeleteAsync("room-1"));
	}

	[Fact]
	public async Task TryDelete_ReturnsFalse_WhenTheRoomDoesNotExist() =>
		Assert.False(await (await NewStoreAsync()).TryDeleteAsync("never-created"));

	// 實作提供一個**空的** store。Postgres 版靠這一步清掉上一條測試留下的資料，所以它得是 async
	// ——in-memory 版 new 一個就好，兩者的差別只在這裡。
	protected abstract ValueTask<IRoomStore> NewStoreAsync();

	private async Task<IRoomStore> NewStoreWithAsync(params Room[] rooms)
	{
		var store = await NewStoreAsync();

		foreach (var room in rooms)
			Assert.True(await store.TryCreateAsync(room));

		return store;
	}

	private static Room NewRoom(
		string roomId = "room-1",
		string? passwordHash = null,
		string ownerUserId = "user-1") =>
		new(roomId, "Lobby", passwordHash, ownerUserId, _CreatedAt);
}
