using Common.Rooms;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Rooms;

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
		StubHash(database, "room-1", ToEntries(NewRoom(passwordHash: "hashed", isClosed: true)));

		var room = await store.GetAsync("room-1");

		Assert.NotNull(room);
		Assert.Equal("room-1", room.RoomId);
		Assert.Equal("Lobby", room.Name);
		Assert.Equal("hashed", room.PasswordHash);
		Assert.Equal("user-1", room.OwnerUserId);
		Assert.Equal(_CreatedAt, room.CreatedAt);
		Assert.True(room.IsClosed);
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
	public async Task ListOpenAsync_SkipsClosedRooms_AndOrphanedIndexEntries()
	{
		var (store, database) = CreateStore();
		database
			.SetMembersAsync((RedisKey)"{rooms}:index", Arg.Any<CommandFlags>())
			.Returns([(RedisValue)"open", (RedisValue)"closed", (RedisValue)"orphan"]);

		StubHash(database, "open", ToEntries(NewRoom(roomId: "open")));
		StubHash(database, "closed", ToEntries(NewRoom(roomId: "closed", isClosed: true)));
		StubHash(database, "orphan", []); // index 有 id 但 hash 不存在：TryCreateAsync 中途失敗的殘留

		var rooms = await store.ListOpenAsync();

		Assert.Equal(["open"], rooms.Select(room => room.RoomId));
	}

	[Fact]
	public async Task TryCloseAsync_SetsIsClosed_WhenTheRoomIsOpen()
	{
		var (store, database) = CreateStore();
		StubIsClosed(database, "room-1", "0");

		Assert.True(await store.TryCloseAsync("room-1"));
		await database.Received(1).HashSetAsync(
			(RedisKey)"{rooms}:room:room-1",
			(RedisValue)"is_closed",
			(RedisValue)"1",
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryCloseAsync_ReturnsFalse_WhenAlreadyClosed()
	{
		var (store, database) = CreateStore();
		StubIsClosed(database, "room-1", "1");

		Assert.False(await store.TryCloseAsync("room-1"));
		await database.DidNotReceive().HashSetAsync(
			Arg.Any<RedisKey>(),
			Arg.Any<RedisValue>(),
			Arg.Any<RedisValue>(),
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryCloseAsync_ReturnsFalse_WhenTheRoomDoesNotExist()
	{
		var (store, database) = CreateStore();
		StubIsClosed(database, "room-1", RedisValue.Null);

		Assert.False(await store.TryCloseAsync("room-1"));
	}

	[Fact]
	public async Task TryUpdateSettingsAsync_WritesNameAndPasswordHash()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", ToEntries(NewRoom()));

		Assert.True(await store.TryUpdateSettingsAsync("room-1", "Renamed", "newhash"));
		await database.Received(1).HashSetAsync(
			(RedisKey)"{rooms}:room:room-1",
			Arg.Is<HashEntry[]>(entries => HasField(entries, "name", "Renamed") && HasField(entries, "password_hash", "newhash")),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryUpdateSettingsAsync_ReturnsFalse_WhenTheRoomIsClosed()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", ToEntries(NewRoom(isClosed: true)));

		Assert.False(await store.TryUpdateSettingsAsync("room-1", "Renamed", null));
		await database.DidNotReceive().HashSetAsync(
			Arg.Any<RedisKey>(),
			Arg.Any<HashEntry[]>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryUpdateSettingsAsync_ReturnsFalse_WhenTheRoomDoesNotExist()
	{
		var (store, database) = CreateStore();
		StubHash(database, "room-1", []);

		Assert.False(await store.TryUpdateSettingsAsync("room-1", "Renamed", null));
	}

	private static (IRoomStore Store, IDatabase Database) CreateStore()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisRoomStore(multiplexer), database);
	}

	private static Room NewRoom(string roomId = "room-1", string? passwordHash = null, bool isClosed = false) =>
		new(roomId, "Lobby", passwordHash, "user-1", _CreatedAt, isClosed);

	private static HashEntry[] ToEntries(Room room) =>
	[
		new("name", room.Name),
		new("password_hash", room.PasswordHash ?? string.Empty),
		new("owner_user_id", room.OwnerUserId),
		new("created_at", room.CreatedAt.ToUnixTimeMilliseconds()),
		new("is_closed", room.IsClosed ? "1" : "0"),
	];

	private static void StubHash(IDatabase database, string roomId, HashEntry[] entries) =>
		database
			.HashGetAllAsync((RedisKey)$"{{rooms}}:room:{roomId}", Arg.Any<CommandFlags>())
			.Returns(entries);

	private static void StubIsClosed(IDatabase database, string roomId, RedisValue value) =>
		database
			.HashGetAsync((RedisKey)$"{{rooms}}:room:{roomId}", (RedisValue)"is_closed", Arg.Any<CommandFlags>())
			.Returns(value);

	private static bool HasField(HashEntry[]? entries, string name, string value) =>
		entries is not null && entries.Any(entry => entry.Name == name && entry.Value == value);
}
