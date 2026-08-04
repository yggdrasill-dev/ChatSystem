using Common.Identity;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Identity;

public class RedisLoginNonceStoreTests
{
	[Fact]
	public async Task IssueAsync_StoresTheNonce_WithATwoMinuteTtl()
	{
		var (store, database) = CreateStore();

		var nonce = await store.IssueAsync();

		await database.Received(1).StringSetAsync(
			(RedisKey)$"LoginNonce:{nonce}",
			Arg.Any<RedisValue>(),
			(Expiration)TimeSpan.FromMinutes(2),
			Arg.Any<ValueCondition>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task TryConsumeAsync_SucceedsOnce_ThenFailsOnReplay()
	{
		var (store, database) = CreateStore();
		database
			.KeyDeleteAsync((RedisKey)"LoginNonce:nonce-1", Arg.Any<CommandFlags>())
			.Returns(true, false);

		// DEL 的回傳值就是一次性守門——「先 EXISTS 再 DEL」會讓兩個請求都通過
		Assert.True(await store.TryConsumeAsync("nonce-1"));
		Assert.False(await store.TryConsumeAsync("nonce-1"));
	}

	[Fact]
	public async Task TryConsumeAsync_ReturnsFalse_WithoutTouchingRedis_WhenThereIsNoNonce()
	{
		var (store, database) = CreateStore();

		Assert.False(await store.TryConsumeAsync(string.Empty));
		await database.DidNotReceive().KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
	}

	private static (ILoginNonceStore Store, IDatabase Database) CreateStore()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisLoginNonceStore(multiplexer), database);
	}
}
