using Common.Identity;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Identity;

public class RedisSessionStoreTests
{
	private static readonly TimeSpan _Ttl = TimeSpan.FromDays(7);

	[Fact]
	public async Task CreateSessionAsync_StoresTheUserIdUnderANewToken_WithTheSevenDayTtl()
	{
		var (store, database) = CreateStore();

		var sessionToken = await store.CreateSessionAsync("user-1");

		await database.Received(1).StringSetAsync(
			(RedisKey)$"Session:{sessionToken}",
			(RedisValue)"user-1",
			(Expiration)_Ttl,
			Arg.Any<ValueCondition>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task CreateSessionAsync_ReturnsADifferentOpaqueTokenEachTime()
	{
		var (store, _) = CreateStore();

		var first = await store.CreateSessionAsync("user-1");
		var second = await store.CreateSessionAsync("user-1");

		// token 是 opaque 隨機值，不帶 userId、不自我驗證（identity-layer.md ADR-2）
		Assert.NotEqual(first, second);
		Assert.Equal(64, first.Length);
		Assert.DoesNotContain("user-1", first);
		Assert.All(first, character => Assert.Contains(character, "0123456789abcdef"));
	}

	[Fact]
	public async Task ResolveUserIdAsync_ReturnsTheUserId()
	{
		var (store, database) = CreateStore();
		database.StringGetAsync((RedisKey)"Session:token-1", Arg.Any<CommandFlags>()).Returns((RedisValue)"user-1");

		Assert.Equal("user-1", await store.ResolveUserIdAsync("token-1"));
	}

	[Fact]
	public async Task ResolveUserIdAsync_ReturnsNull_WhenTheSessionIsGone()
	{
		var (store, database) = CreateStore();
		database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

		Assert.Null(await store.ResolveUserIdAsync("token-1"));
	}

	[Fact]
	public async Task ResolveUserIdAsync_DoesNotQueryRedis_WhenThereIsNoToken()
	{
		var (store, database) = CreateStore();

		// 呼叫端是「cookie 可能不存在」的 handshake 路徑
		Assert.Null(await store.ResolveUserIdAsync(string.Empty));
		await database.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task RefreshAsync_ExtendsTheTtl()
	{
		var (store, database) = CreateStore();

		await store.RefreshAsync("token-1");

		await database.Received(1).KeyExpireAsync(
			(RedisKey)"Session:token-1",
			_Ttl,
			Arg.Any<ExpireWhen>(),
			Arg.Any<CommandFlags>());
	}

	[Fact]
	public async Task RevokeAsync_DeletesTheSession()
	{
		var (store, database) = CreateStore();

		await store.RevokeAsync("token-1");

		await database.Received(1).KeyDeleteAsync((RedisKey)"Session:token-1", Arg.Any<CommandFlags>());
	}

	private static (ISessionStore Store, IDatabase Database) CreateStore()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (new RedisSessionStore(multiplexer), database);
	}
}
