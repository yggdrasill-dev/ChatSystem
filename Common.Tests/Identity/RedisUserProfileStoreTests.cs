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
