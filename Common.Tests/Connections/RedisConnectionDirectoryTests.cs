using Common.Connections;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Connections;

public class RedisConnectionDirectoryTests
{
	[Fact]
	public async Task RegisterAsync_SetsConnKeyToNodeId_WithExpiry()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		var directory = new RedisConnectionDirectory(multiplexer);

		await directory.RegisterAsync("conn-1", "node-1");

		await database.Received(1).StringSetAsync(
			(RedisKey)"Conn:conn-1",
			(RedisValue)"node-1",
			Arg.Any<Expiration>(),
			Arg.Any<ValueCondition>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task UnregisterAsync_DeletesConnKey()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		var directory = new RedisConnectionDirectory(multiplexer);

		await directory.UnregisterAsync("conn-1");

		await database.Received(1).KeyDeleteAsync((RedisKey)"Conn:conn-1", Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task ResolveNodesAsync_OmitsConnectionIds_ThatAreNotFound()
	{
		var database = Substitute.For<IDatabase>();
		database.StringGetAsync((RedisKey)"Conn:conn-a", Arg.Any<CommandFlags>()).Returns((RedisValue)"node-1");
		database.StringGetAsync((RedisKey)"Conn:conn-b", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		var directory = new RedisConnectionDirectory(multiplexer);

		var result = await directory.ResolveNodesAsync(["conn-a", "conn-b"]);

		Assert.Equal(new Dictionary<string, string> { ["conn-a"] = "node-1" }, result);
	}
}
