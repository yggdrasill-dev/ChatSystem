using Common.Identity;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Identity;

public class RedisPresenceDirectoryTests
{
	[Fact]
	public async Task BindAsync_WritesTheNewConnectionId_AndReturnsTheSupersededOne()
	{
		var (directory, database) = CreateDirectory();
		StubBind(database, "user-1", (RedisValue)"conn-old");

		var previous = await directory.BindConnectionAsync("user-1", "conn-new");

		// 取回舊值與寫入新值必須是同一個原子操作，否則兩條同時綁定的連線可能都以為自己
		// 沒有前任，留下一條收不到 fan-out 的孤兒連線
		Assert.Equal("conn-old", previous);
		await database.Received(1).StringSetAndGetAsync(
			(RedisKey)"Presence:user-1",
			(RedisValue)"conn-new",
			Arg.Any<TimeSpan?>(),
			Arg.Any<bool>(),
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task BindAsync_ReturnsNull_WhenTheIdentityHadNoConnection()
	{
		var (directory, database) = CreateDirectory();
		StubBind(database, "user-1", RedisValue.Null);

		Assert.Null(await directory.BindConnectionAsync("user-1", "conn-new"));
	}

	[Fact]
	public async Task BindAsync_SetsNoExpiry()
	{
		var (directory, database) = CreateDirectory();
		StubBind(database, "user-1", RedisValue.Null);

		await directory.BindConnectionAsync("user-1", "conn-new");

		// 正向 key 刻意不設 TTL（identity-layer.md ADR-7）：殘留的舊值無害，
		// 下次綁定會覆寫，而身分層沒有心跳機制可以續期
		await database.Received(1).StringSetAndGetAsync(
			Arg.Any<RedisKey>(),
			Arg.Any<RedisValue>(),
			null,
			Arg.Any<bool>(),
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task UnbindAsync_ComparesBeforeDeleting_AndTouchesOnlyItsOwnKey()
	{
		var (directory, database) = CreateDirectory();

		await directory.UnbindConnectionAsync("user-1", "conn-1");

		// fencing：Supersede 時舊連線的解綁會晚於新連線的綁定，無條件刪除會讓一個在線的
		// 使用者從 fan-out 名單消失。單一 key 是 Cluster 的要求（Presence 沒有 hash tag）
		await database.Received(1).ScriptEvaluateAsync(
			Arg.Is<string>(script =>
				script!.Contains("GET") && script.Contains("DEL") && script.Contains("ARGV[1]")),
			Arg.Is<RedisKey[]>(keys => keys != null && keys.Length == 1 && keys[0] == (RedisKey)"Presence:user-1"),
			Arg.Is<RedisValue[]>(values => values != null && values.Length == 1 && values[0] == (RedisValue)"conn-1"),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task ResolveConnectionsAsync_OmitsIdentitiesThatAreNotOnline()
	{
		var (directory, database) = CreateDirectory();
		database.StringGetAsync((RedisKey)"Presence:user-a", Arg.Any<CommandFlags>()).Returns((RedisValue)"conn-a");
		database.StringGetAsync((RedisKey)"Presence:user-b", Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

		var connectionIds = await directory.ResolveConnectionsAsync(["user-a", "user-b"]);

		Assert.Equal(["conn-a"], connectionIds);
	}

	[Fact]
	public async Task ResolveConnectionsAsync_CapsConcurrentRedisLookups()
	{
		const int maxConcurrentLookups = 64;
		var current = 0;
		var observedMax = 0;

		var (directory, database) = CreateDirectory();
		database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(async _ =>
		{
			var concurrentNow = Interlocked.Increment(ref current);
			InterlockedMax(ref observedMax, concurrentNow);

			await Task.Delay(20);

			Interlocked.Decrement(ref current);
			return (RedisValue)"conn-1";
		});

		// 一間房可能有很多成員，fan-out 不能一次對 Redis 開出等量的平行查詢
		await directory.ResolveConnectionsAsync([.. Enumerable.Range(0, maxConcurrentLookups * 4).Select(i => $"user-{i}")]);

		Assert.True(
			observedMax <= maxConcurrentLookups,
			$"Expected at most {maxConcurrentLookups} concurrent lookups, observed {observedMax}.");
	}

	private static (IPresenceDirectory Directory, IDatabase Database) CreateDirectory()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisPresenceDirectory(multiplexer), database);
	}

	private static void StubBind(IDatabase database, string userId, RedisValue previous) =>
		database
			.StringSetAndGetAsync(
				(RedisKey)$"Presence:{userId}",
				Arg.Any<RedisValue>(),
				Arg.Any<TimeSpan?>(),
				Arg.Any<bool>(),
				Arg.Any<When>(),
				Arg.Any<CommandFlags>())
			.Returns(previous);

	private static void InterlockedMax(ref int location, int value)
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
