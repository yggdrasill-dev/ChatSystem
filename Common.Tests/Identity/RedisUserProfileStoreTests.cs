using Common.Identity;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Identity;

public class RedisUserProfileStoreTests
{
	[Fact]
	public async Task SaveAsync_WritesTheDisplayNameAndPicture()
	{
		var (store, database) = CreateStore();

		await store.SaveAsync("user-1", "Sunny", "https://example/pic");

		await database.Received(1).HashSetAsync(
			(RedisKey)"Profile:user-1",
			Arg.Is<HashEntry[]>(entries =>
				HasField(entries, "display_name", "Sunny") && HasField(entries, "picture_url", "https://example/pic")),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task SaveAsync_WritesEmptyStrings_WhenGoogleGaveNothing()
	{
		var (store, database) = CreateStore();

		await store.SaveAsync("user-1", null, null);

		// 空字串代表「沒有值」，跟 RedisRoomStore 對 password_hash 的處理一致
		await database.Received(1).HashSetAsync(
			(RedisKey)"Profile:user-1",
			Arg.Is<HashEntry[]>(entries =>
				HasField(entries, "display_name", string.Empty) && HasField(entries, "picture_url", string.Empty)),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task GetDisplayNameAsync_ReadsASingleField_NotTheWholeHash()
	{
		var (store, database) = CreateStore();
		StubDisplayName(database, "Sunny");

		Assert.Equal("Sunny", await store.GetDisplayNameAsync("user-1"));

		// 單一 HGET 而不是 HGETALL：這是 chat.send 的熱路徑，每則訊息一次（chat-layer.md ADR-3）。
		await database.Received(1).HashGetAsync(
			(RedisKey)"Profile:user-1",
			(RedisValue)"display_name",
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task GetDisplayNameAsync_ReturnsNull_WhenThereIsNoProfile()
	{
		var (store, database) = CreateStore();
		StubDisplayName(database, RedisValue.Null);

		// null（沒有 profile）與空字串（Google 沒給 name）要分得出來——對聊天層是同一個結果，
		// 但介面不該把兩者混成一個。
		Assert.Null(await store.GetDisplayNameAsync("user-1"));
	}

	[Fact]
	public async Task GetDisplayNameAsync_ReturnsEmpty_WhenGoogleGaveNothing()
	{
		var (store, database) = CreateStore();
		StubDisplayName(database, string.Empty);

		Assert.Equal(string.Empty, await store.GetDisplayNameAsync("user-1"));
	}

	private static void StubDisplayName(IDatabase database, RedisValue value) =>
		database
			.HashGetAsync((RedisKey)"Profile:user-1", (RedisValue)"display_name", Arg.Any<CommandFlags>())
			.Returns(value);

	private static (IUserProfileStore Store, IDatabase Database) CreateStore()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisUserProfileStore(multiplexer), database);
	}

	private static bool HasField(HashEntry[]? entries, string name, string value) =>
		entries is not null && entries.Any(entry => entry.Name == name && entry.Value == value);
}
