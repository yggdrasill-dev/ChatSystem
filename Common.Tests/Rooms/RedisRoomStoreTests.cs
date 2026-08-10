using Common.Rooms;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Rooms;

// 這一份守的是 Redis 實作的形狀（哪個 key、哪個指令、什麼順序），介面本身的語意由
// RoomStoreContractTests 守。兩份互補：換成 Postgres 時這一份整份作廢，那一份原封不動接過去。
public class RedisRoomStoreTests
{
	private static readonly DateTimeOffset _CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task TryCreateAsync_ClaimsTheIndexFirst_ThenWritesTheHash()
	{
		var (store, database) = CreateStore();
		database.SetAddAsync((RedisKey)"{rooms}:index", (RedisValue)"room-1", Arg.Any<CommandFlags>()).Returns(true);

		var created = await store.TryCreateAsync(NewRoom());

		Assert.True(created);
		await database.Received(1).HashSetAsync(
			(RedisKey)"{rooms}:room:room-1",
			Arg.Is<HashEntry[]>(entries => HasField(entries, "owner_user_id", "user-1")),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryCreateAsync_ReturnsFalse_AndWritesNothing_WhenTheIdIsAlreadyClaimed()
	{
		var (store, database) = CreateStore();
		database.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(false);

		var created = await store.TryCreateAsync(NewRoom());

		// SADD 的回傳值就是原子守門，這是本介面唯一不能有競爭的操作
		Assert.False(created);
		await database.DidNotReceive().HashSetAsync(
			Arg.Any<RedisKey>(),
			Arg.Any<HashEntry[]>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task GetAsync_ReturnsNull_WhenTheHashIsMissing()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", []);

		Assert.Null(await store.GetAsync("room-1"));
	}

	[Fact]
	public async Task GetAsync_MapsEveryField()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", ToEntries(NewRoom(passwordHash: "hashed")));

		var room = await store.GetAsync("room-1");

		Assert.NotNull(room);
		Assert.Equal("room-1", room.RoomId);
		Assert.Equal("Lobby", room.Name);
		Assert.Equal("hashed", room.PasswordHash);
		Assert.Equal("user-1", room.OwnerUserId);
		Assert.Equal(_CreatedAt, room.CreatedAt);
	}

	[Fact]
	public async Task GetAsync_MapsAnEmptyPasswordHashToNull()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", ToEntries(NewRoom(passwordHash: null)));

		var room = await store.GetAsync("room-1");

		// 空字串是「公開房」的儲存表示，不是「密碼是空字串」
		Assert.Null(room!.PasswordHash);
	}

	[Fact]
	public async Task ListAsync_SkipsOrphanedIndexEntries()
	{
		var (store, database) = CreateStore();
		database
			.SetMembersAsync((RedisKey)"{rooms}:index", Arg.Any<CommandFlags>())
			.Returns([(RedisValue)"live", (RedisValue)"orphan"]);

		StubHash(database, "live", ToEntries(NewRoom(roomId: "live")));

		// index 有 id 但 hash 不存在。ADR-10 之前只有 TryCreateAsync 中途失敗會留下這種殘留，
		// 現在 TryDeleteAsync 中途失敗也會——真刪是「先 hash 後 index」兩步。
		StubHash(database, "orphan", []);

		var rooms = await store.ListAsync();

		Assert.Equal(["live"], rooms.Select(room => room.RoomId));
	}

	[Fact]
	public async Task TryDeleteAsync_DeletesTheHash_TheIndexEntry_AndTheBanList()
	{
		var (store, database) = CreateStore();
		database.KeyDeleteAsync((RedisKey)"{rooms}:room:room-1", Arg.Any<CommandFlags>()).Returns(true);

		Assert.True(await store.TryDeleteAsync("room-1"));

		await database.Received(1).SetRemoveAsync(
			(RedisKey)"{rooms}:index",
			(RedisValue)"room-1",
			Arg.Any<CommandFlags>());

		// **Redis 沒有 ON DELETE CASCADE**，封鎖名單只能自己帶走。這條保證進不了契約測試
		// （in-memory 的兩個替身是獨立物件），只有這裡守得到。
		await database.Received(1).KeyDeleteAsync((RedisKey)"{rooms}:ban:room-1", Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryDeleteAsync_ReturnsFalse_AndLeavesTheIndexAlone_WhenTheRoomDoesNotExist()
	{
		var (store, database) = CreateStore();
		database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(false);

		// DEL 的回傳值就是原子守門，對稱於 TryCreateAsync 的 SADD：兩個併發的 close 只有一個
		// 刪得到，所以 RoomClosed 不會被廣播兩次。
		Assert.False(await store.TryDeleteAsync("room-1"));

		await database.DidNotReceive().SetRemoveAsync(
			Arg.Any<RedisKey>(),
			Arg.Any<RedisValue>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryUpdateSettingsAsync_WritesNameAndPasswordHash_ThroughTheGuardedScript()
	{
		var (store, database) = CreateStore();
		StubUpdateScript(database, 1);

		Assert.True(await store.TryUpdateSettingsAsync("room-1", "Renamed", "newhash"));

		await database.Received(1).ScriptEvaluateAsync(
			Arg.Is<string>(script => script.Contains("EXISTS")),
			Arg.Is<RedisKey[]>(keys => keys.Length == 1 && keys[0] == (RedisKey)"{rooms}:room:room-1"),
			Arg.Is<RedisValue[]>(values =>
				HasPair(values, "name", "Renamed") && HasPair(values, "password_hash", "newhash")),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryUpdateSettingsAsync_ReturnsFalse_WhenTheScriptFindsNoRoom()
	{
		var (store, database) = CreateStore();
		StubUpdateScript(database, 0);

		// **守門必須在 Lua 裡而不是先讀一次再寫。** HSET 對不存在的 key 會建立它，所以
		// update 撞上 delete 會把刪掉的房間寫回來一半（只有 name 與 password_hash、沒有房主）。
		// ADR-10 之前守門是 existing.IsClosed，真刪之後沒有那個欄位可以看了。
		Assert.False(await store.TryUpdateSettingsAsync("room-1", "Renamed", null));
	}

	private static (IRoomStore Store, IDatabase Database) CreateStore()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisRoomStore(multiplexer), database);
	}

	private static Room NewRoom(string roomId = "room-1", string? passwordHash = null) =>
		new(roomId, "Lobby", passwordHash, "user-1", _CreatedAt);

	private static HashEntry[] ToEntries(Room room) =>
	[
		new("name", room.Name),
		new("password_hash", room.PasswordHash ?? string.Empty),
		new("owner_user_id", room.OwnerUserId),
		new("created_at", room.CreatedAt.ToUnixTimeMilliseconds()),
	];

	private static void StubHash(IDatabase database, string roomId, HashEntry[] entries) =>
		database
			.HashGetAllAsync((RedisKey)$"{{rooms}}:room:{roomId}", Arg.Any<CommandFlags>())
			.Returns(entries);

	private static void StubUpdateScript(IDatabase database, long result) =>
		database
			.ScriptEvaluateAsync(
				Arg.Any<string>(),
				Arg.Any<RedisKey[]>(),
				Arg.Any<RedisValue[]>(),
				Arg.Any<CommandFlags>())
			.Returns(RedisResult.Create((RedisValue)result));

	private static bool HasField(HashEntry[]? entries, string name, string value) =>
		entries is not null && entries.Any(entry => entry.Name == name && entry.Value == value);

	// 腳本的 ARGV 是 field/value 交錯的，所以成對比而不是逐格比。
	private static bool HasPair(RedisValue[]? values, string field, string value)
	{
		if (values is null)
			return false;

		for (var i = 0; i + 1 < values.Length; i += 2)
		{
			if (values[i] == field && values[i + 1] == value)
				return true;
		}

		return false;
	}
}
