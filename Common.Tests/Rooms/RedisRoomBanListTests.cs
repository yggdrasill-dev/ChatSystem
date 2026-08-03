using Common.Rooms;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Rooms;

public class RedisRoomBanListTests
{
	[Fact]
	public async Task IsBannedAsync_ChecksTheRoomsBanSet()
	{
		var (banList, database) = CreateBanList();
		database
			.SetContainsAsync((RedisKey)"{rooms}:ban:room-1", (RedisValue)"user-1", Arg.Any<CommandFlags>())
			.Returns(true);

		Assert.True(await banList.IsBannedAsync("room-1", "user-1"));
		Assert.False(await banList.IsBannedAsync("room-1", "user-2"));
	}

	[Fact]
	public async Task BanAsync_AddsToTheRoomsBanSet()
	{
		var (banList, database) = CreateBanList();

		await banList.BanAsync("room-1", "user-1");

		await database.Received(1).SetAddAsync(
			(RedisKey)"{rooms}:ban:room-1",
			(RedisValue)"user-1",
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task UnbanAsync_RemovesFromTheRoomsBanSet()
	{
		var (banList, database) = CreateBanList();

		await banList.UnbanAsync("room-1", "user-1");

		await database.Received(1).SetRemoveAsync(
			(RedisKey)"{rooms}:ban:room-1",
			(RedisValue)"user-1",
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task BanAsync_IsIdempotent()
	{
		var (banList, database) = CreateBanList();

		// 集合操作本身就是原子且 idempotent 的，所以 IRoomBanList 不需要 Try 語意
		await banList.BanAsync("room-1", "user-1");
		await banList.BanAsync("room-1", "user-1");

		await database.Received(2).SetAddAsync(
			(RedisKey)"{rooms}:ban:room-1",
			(RedisValue)"user-1",
			Arg.Any<CommandFlags>());
	}

	private static (IRoomBanList BanList, IDatabase Database) CreateBanList()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisRoomBanList(multiplexer), database);
	}
}
