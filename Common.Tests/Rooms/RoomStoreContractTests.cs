using Common.Rooms;

namespace Common.Tests.Rooms;

// **這些是 IRoomStore 的契約，不是替身的規格。** 階段 B 的 PostgresRoomStore 必須通過同一組
// 斷言——屆時把 NewStore() 換掉就行，形狀比照 ChatMessageStoreContractTests。
//
// 房間層原本沒有這個東西，這是 RedisRoomStoreTests 蓋不到的那一半：那 12 條斷言的是 Redis
// 指令的形狀（SADD 哪個 key、HashSetAsync 哪些欄位），換掉實作整份作廢、一條都接不過去。
// 這裡只寫「任何儲存都必須成立」的語意，所以兩份是互補而不是重複。
//
// 標了 [ADR-10] 的斷言會在關房改成真刪那一步消失或翻面（chat-layer.md ADR-10）。標記留著是
// 為了讓那一步是機械式的——不用重新判斷哪幾條該動。
public class RoomStoreContractTests
{
	// 固定值而不是 UtcNow：Redis 版把 CreatedAt 存成 unix 毫秒，UtcNow 帶的次毫秒 ticks
	// round-trip 之後會被截掉。**契約的精度上限就是毫秒**，Postgres 的 timestamptz 更精確
	// 也不改變這一點——這是介面允許的最小保證，不是實作的能力。
	private static readonly DateTimeOffset _CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task Get_ReturnsNull_WhenTheRoomDoesNotExist() =>
		Assert.Null(await NewStore().GetAsync("never-created"));

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
		Assert.False(room.IsClosed);
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
	public async Task ListOpen_ReturnsEmpty_WhenThereAreNoRooms() =>
		Assert.Empty(await NewStore().ListOpenAsync());

	[Fact]
	public async Task ListOpen_ReturnsEveryRoom()
	{
		var store = await NewStoreWithAsync(NewRoom("room-1"), NewRoom("room-2"));

		// **順序不在契約裡**，所以排序過再比：Redis 走 SMEMBERS（集合無序），Postgres 沒有
		// ORDER BY 的 SELECT 同樣不保證順序。要是哪天 room.list 需要固定順序，那是新增一條
		// 契約而不是「本來就有」。
		Assert.Equal(["room-1", "room-2"], (await store.ListOpenAsync()).Select(room => room.RoomId).Order());
	}

	[Fact]
	public async Task ListOpen_ExcludesClosedRooms()
	{
		// [ADR-10] 關房改成真刪之後這條消失：刪掉的房間不在裡面是 DELETE 的性質，不需要斷言。
		var store = await NewStoreWithAsync(NewRoom("room-1"), NewRoom("room-2"));

		Assert.True(await store.TryCloseAsync("room-2"));

		Assert.Equal(["room-1"], (await store.ListOpenAsync()).Select(room => room.RoomId));
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
		// ADR-10 之後這會是 false 唯一的來源（Postgres 上就是 rowcount = 0）。
		Assert.False(await NewStore().TryUpdateSettingsAsync("never-created", "Lounge", null));

	[Fact]
	public async Task TryUpdateSettings_ReturnsFalse_WhenTheRoomIsClosed()
	{
		// [ADR-10] 真刪之後這條併進上面那條：房間不在了就是不在了，沒有「已關閉」這個中間狀態。
		var store = await NewStoreWithAsync(NewRoom());

		Assert.True(await store.TryCloseAsync("room-1"));

		Assert.False(await store.TryUpdateSettingsAsync("room-1", "Lounge", null));
	}

	[Fact]
	public async Task TryClose_ClosesAnOpenRoom()
	{
		// [ADR-10] 改成 TryDeleteAsync：斷言從「IsClosed 變成 true」翻成「GetAsync 回 null」。
		var store = await NewStoreWithAsync(NewRoom());

		Assert.True(await store.TryCloseAsync("room-1"));
		Assert.True((await store.GetAsync("room-1"))!.IsClosed);
	}

	[Fact]
	public async Task TryClose_ReturnsFalse_WhenTheRoomIsAlreadyClosed()
	{
		// [ADR-10] 真刪之後這條併進下面那條——第二次刪就是「房間不存在」。回 false 保住的
		// idempotency 不變，只是理由換了一個。
		var store = await NewStoreWithAsync(NewRoom());

		Assert.True(await store.TryCloseAsync("room-1"));
		Assert.False(await store.TryCloseAsync("room-1"));
	}

	[Fact]
	public async Task TryClose_ReturnsFalse_WhenTheRoomDoesNotExist() =>
		// 「沒有實際生效就回 false」是 RoomCloseHandler 決定要不要廣播 RoomClosed 的依據，
		// 所以這條不是防禦性斷言。
		Assert.False(await NewStore().TryCloseAsync("never-created"));

	private static IRoomStore NewStore() => new InMemoryRoomStore();

	private static async Task<IRoomStore> NewStoreWithAsync(params Room[] rooms)
	{
		var store = NewStore();

		foreach (var room in rooms)
			Assert.True(await store.TryCreateAsync(room));

		return store;
	}

	// 一律建成開著的房間，關閉狀態只能經由 TryCloseAsync 產生：契約測試不該自己組出一個
	// 中間狀態塞進去，那會驗到「儲存能不能存下這個欄位」而不是「介面的行為」。
	private static Room NewRoom(
		string roomId = "room-1",
		string? passwordHash = null,
		string ownerUserId = "user-1") =>
		new(roomId, "Lobby", passwordHash, ownerUserId, _CreatedAt, false);
}
