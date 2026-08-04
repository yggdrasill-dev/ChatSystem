using Common.Rooms;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Rooms;

public class RedisRoomMembershipTests
{
	private static readonly DateTimeOffset _Now = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public async Task JoinAsync_WritesTheMember_PointsTheUserAtTheRoom_AndClearsTheGraceEntry()
	{
		var (membership, database) = CreateMembership();

		await membership.JoinAsync("room-1", "user-1", "conn-1");

		await database.Received(1).HashSetAsync(
			(RedisKey)"{rooms}:members:room-1",
			(RedisValue)"user-1",
			(RedisValue)$"{_Now.ToUnixTimeMilliseconds()}|conn-1|",
			Arg.Any<When>(),
			Arg.Any<CommandFlags>());

		await database.Received(1).StringSetAsync(
			(RedisKey)"{rooms}:userroom:user-1",
			(RedisValue)"room-1",
			Arg.Any<Expiration>(),
			Arg.Any<ValueCondition>(),
			Arg.Any<CommandFlags>());

		// 這個人回來了，sweeper 不該再處理他
		await database.Received(1).SortedSetRemoveAsync(
			(RedisKey)"{rooms}:grace",
			(RedisValue)"room-1|user-1",
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task GetMembersAsync_MapsEveryField()
	{
		var (membership, database) = CreateMembership();
		StubMembers(database, "room-1", [new HashEntry("user-1", $"{_Now.ToUnixTimeMilliseconds()}|conn-1|")]);

		var member = Assert.Single(await membership.GetMembersAsync("room-1"));

		Assert.Equal("user-1", member.UserId);
		Assert.Equal(_Now, member.JoinedAt);
		Assert.Equal("conn-1", member.CurrentConnectionId);
		Assert.Null(member.DisconnectedAt);
	}

	[Fact]
	public async Task GetMembersAsync_KeepsMembersStillInsideTheGracePeriod()
	{
		var (membership, database) = CreateMembership();
		var disconnectedAt = _Now - TimeSpan.FromSeconds(20);
		StubMembers(database, "room-1", [
			new HashEntry("user-1", $"{_Now.ToUnixTimeMilliseconds()}|conn-1|{disconnectedAt.ToUnixTimeMilliseconds()}"),
		]);

		var member = Assert.Single(await membership.GetMembersAsync("room-1"));

		// 寬限期內還算成員：斷線重連對其他成員應該完全無感
		Assert.Equal(disconnectedAt, member.DisconnectedAt);
	}

	[Fact]
	public async Task GetMembersAsync_FiltersOutMembersWhoseGracePeriodExpired()
	{
		var (membership, database) = CreateMembership();
		var expired = _Now - RedisRoomMembership.GracePeriod - TimeSpan.FromSeconds(1);
		StubMembers(database, "room-1", [
			new HashEntry("stale", $"{_Now.ToUnixTimeMilliseconds()}|conn-1|{expired.ToUnixTimeMilliseconds()}"),
			new HashEntry("live", $"{_Now.ToUnixTimeMilliseconds()}|conn-2|"),
		]);

		var members = await membership.GetMembersAsync("room-1");

		// 過濾發生在讀取時，所以正確性不依賴 sweeper 有沒有跑（ADR-2）
		Assert.Equal(["live"], members.Select(member => member.UserId));
	}

	[Fact]
	public async Task GetMembersAsync_SkipsUnparseableEntries()
	{
		var (membership, database) = CreateMembership();
		StubMembers(database, "room-1", [new HashEntry("broken", "nonsense")]);

		Assert.Empty(await membership.GetMembersAsync("room-1"));
	}

	[Fact]
	public async Task RemoveAsync_DeletesTheMember_AndReleasesTheUserRoomPointerWithFencing()
	{
		var (membership, database) = CreateMembership();

		await membership.RemoveAsync("room-1", "user-1");

		await database.Received(1).HashDeleteAsync(
			(RedisKey)"{rooms}:members:room-1",
			(RedisValue)"user-1",
			Arg.Any<CommandFlags>());

		// userId → roomId 只在還指向這個房間時才刪：使用者可能已經加入別的房間了
		await database.Received(1).ScriptEvaluateAsync(
			Arg.Is<string>(script => script!.Contains("GET") && script.Contains("DEL")),
			Arg.Is<RedisKey[]>(keys => keys != null && keys.Length == 1 && keys[0] == (RedisKey)"{rooms}:userroom:user-1"),
			Arg.Is<RedisValue[]>(values => values != null && values.Length == 1 && values[0] == (RedisValue)"room-1"),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task MarkDisconnectedAsync_DoesNothing_WhenTheUserIsNotInAnyRoom()
	{
		var (membership, database) = CreateMembership();
		StubCurrentRoom(database, "user-1", RedisValue.Null);

		await membership.MarkDisconnectedAsync("user-1", "conn-1");

		await database.DidNotReceive().ScriptEvaluateAsync(
			Arg.Any<string>(),
			Arg.Any<RedisKey[]>(),
			Arg.Any<RedisValue[]>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task MarkDisconnectedAsync_FencesOnTheConnectionId_AndSchedulesTheSweeper()
	{
		var (membership, database) = CreateMembership();
		StubCurrentRoom(database, "user-1", "room-1");

		await membership.MarkDisconnectedAsync("user-1", "conn-1");

		// 檢查 fencing、寫入 DisconnectedAt、排進 sweeper 待辦，三件事在同一段 Lua 裡：
		// 只做前兩件的話 sweeper 永遠不知道，只做第三件的話讀取時的過濾會漏掉
		await database.Received(1).ScriptEvaluateAsync(
			Arg.Is<string>(script =>
				script!.Contains("HGET") && script.Contains("HSET") && script.Contains("ZADD") &&
				script.Contains("ARGV[2]")),
			Arg.Is<RedisKey[]>(keys => keys != null && keys.Length == 2 &&
				keys[0] == (RedisKey)"{rooms}:members:room-1" && keys[1] == (RedisKey)"{rooms}:grace"),
			Arg.Is<RedisValue[]>(values => values != null && values.Length == 5 &&
				values[0] == (RedisValue)"user-1" &&
				values[1] == (RedisValue)"conn-1" &&
				values[4] == (RedisValue)"room-1|user-1"),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task MarkDisconnectedAsync_SchedulesTheSweeperOneGracePeriodOut()
	{
		var (membership, database) = CreateMembership();
		StubCurrentRoom(database, "user-1", "room-1");

		await membership.MarkDisconnectedAsync("user-1", "conn-1");

		var expectedExpiry = (_Now + RedisRoomMembership.GracePeriod).ToUnixTimeMilliseconds();

		await database.Received(1).ScriptEvaluateAsync(
			Arg.Any<string>(),
			Arg.Any<RedisKey[]>(),
			Arg.Is<RedisValue[]>(values => values != null &&
				values[2] == (RedisValue)_Now.ToUnixTimeMilliseconds() &&
				values[3] == (RedisValue)expectedExpiry),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task ListExpiredAsync_ReadsEverythingDueFromTheSortedSet()
	{
		var (membership, database) = CreateMembership();
		database
			.SortedSetRangeByScoreAsync(
				(RedisKey)"{rooms}:grace",
				double.NegativeInfinity,
				(double)_Now.ToUnixTimeMilliseconds(),
				Arg.Any<Exclude>(),
				Arg.Any<Order>(),
				Arg.Any<long>(),
				Arg.Any<long>(),
				Arg.Any<CommandFlags>())
			.Returns([(RedisValue)"room-1|user-1", (RedisValue)"room-2|user-2", (RedisValue)"malformed"]);

		var expired = await membership.ListExpiredAsync();

		// 一次 ZRANGEBYSCORE，不必掃過所有房間；解不開的項目跳過而不是整批失敗
		Assert.Equal([("room-1", "user-1"), ("room-2", "user-2")], expired);
	}

	private static (IRoomMembership Membership, IDatabase Database) CreateMembership()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisRoomMembership(multiplexer, new FixedTimeProvider(_Now)), database);
	}

	private static void StubMembers(IDatabase database, string roomId, HashEntry[] entries) =>
		database
			.HashGetAllAsync((RedisKey)$"{{rooms}}:members:{roomId}", Arg.Any<CommandFlags>())
			.Returns(entries);

	private static void StubCurrentRoom(IDatabase database, string userId, RedisValue roomId) =>
		database
			.StringGetAsync((RedisKey)$"{{rooms}}:userroom:{userId}", Arg.Any<CommandFlags>())
			.Returns(roomId);

	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}
}
