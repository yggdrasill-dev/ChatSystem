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

	[Fact]
	public async Task ResolveNodesAsync_CapsConcurrentRedisLookups()
	{
		const int maxConcurrentLookups = 64;
		var current = 0;
		var observedMax = 0;

		var database = Substitute.For<IDatabase>();
		database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(async _ =>
		{
			var concurrentNow = Interlocked.Increment(ref current);
			InterlockedExtensions.Max(ref observedMax, concurrentNow);

			await Task.Delay(20);

			Interlocked.Decrement(ref current);
			return (RedisValue)"node-1";
		});

		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		var directory = new RedisConnectionDirectory(multiplexer);
		var connectionIds = Enumerable.Range(0, maxConcurrentLookups * 4).Select(i => $"conn-{i}").ToArray();

		await directory.ResolveNodesAsync(connectionIds);

		Assert.True(observedMax <= maxConcurrentLookups, $"Expected at most {maxConcurrentLookups} concurrent lookups, observed {observedMax}.");
	}

	private static class InterlockedExtensions
	{
		public static void Max(ref int location, int value)
		{
			int initial;
			do
			{
				initial = location;
				if (value <= initial)
					return;
			}
			while (Interlocked.CompareExchange(ref location, value, initial) != initial);
		}
	}
}
