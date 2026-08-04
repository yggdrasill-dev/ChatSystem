using Common.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using StackExchange.Redis;

namespace Common.Tests.Identity;

public class IdentityLayerRegistrationTests
{
	private const string IdentityStore = "identity-store";
	private const string ConnectionDirectory = "connection-directory";

	[Fact]
	public async Task AddIdentityStores_ResolvesEveryStore_FromItsOwnKeyedRedisClient()
	{
		var (provider, identityDatabase, otherDatabase) = BuildProvider();

		using (provider)
		{
			await provider.GetRequiredService<ISessionStore>().RevokeAsync("token-1");
			await provider.GetRequiredService<ILoginNonceStore>().TryConsumeAsync("nonce-1");
			await provider.GetRequiredService<IPresenceDirectory>().UnbindAsync("user-1", "conn-1");

			await identityDatabase.Received(1).KeyDeleteAsync((RedisKey)"Session:token-1", Arg.Any<CommandFlags>());
			await identityDatabase.Received(1).KeyDeleteAsync((RedisKey)"LoginNonce:nonce-1", Arg.Any<CommandFlags>());
			await identityDatabase.Received(1).ScriptEvaluateAsync(
				Arg.Any<string>(),
				Arg.Any<RedisKey[]>(),
				Arg.Any<RedisValue[]>(),
				Arg.Any<CommandFlags>());

			// 身分層不共用連線層的 Redis：Session 是持久資料要開 AOF/RDB，
			// connection-directory 是純快取（connection-layer.md ADR-2 的擁有權原則）
			Assert.Empty(otherDatabase.ReceivedCalls());
		}
	}

	[Fact]
	public void AddIdentityStores_RegistersEveryStoreAsSingleton()
	{
		var (provider, _, _) = BuildProvider();

		using (provider)
		{
			Assert.Same(provider.GetRequiredService<ISessionStore>(), provider.GetRequiredService<ISessionStore>());
			Assert.Same(provider.GetRequiredService<ILoginNonceStore>(), provider.GetRequiredService<ILoginNonceStore>());
			Assert.Same(provider.GetRequiredService<IPresenceDirectory>(), provider.GetRequiredService<IPresenceDirectory>());
		}
	}

	[Fact]
	public void AddIdentityStores_FailsFast_WhenTheKeyedRedisClientIsMissing()
	{
		var services = new ServiceCollection();
		services.AddIdentityStores(IdentityStore);

		using var provider = services.BuildServiceProvider();

		// 忘了 builder.AddKeyedRedisClient(...) 的話，第一次解析就炸而不是第一則訊息才炸
		Assert.Throws<InvalidOperationException>(provider.GetRequiredService<ISessionStore>);
	}

	private static (ServiceProvider Provider, IDatabase IdentityDatabase, IDatabase OtherDatabase) BuildProvider()
	{
		var (identityMultiplexer, identityDatabase) = CreateMultiplexer();
		var (otherMultiplexer, otherDatabase) = CreateMultiplexer();

		var services = new ServiceCollection();
		services.AddKeyedSingleton(IdentityStore, identityMultiplexer);
		services.AddKeyedSingleton(ConnectionDirectory, otherMultiplexer);
		services.AddIdentityStores(IdentityStore);

		return (services.BuildServiceProvider(), identityDatabase, otherDatabase);
	}

	private static (IConnectionMultiplexer Multiplexer, IDatabase Database) CreateMultiplexer()
	{
		var database = Substitute.For<IDatabase>();
		var multiplexer = Substitute.For<IConnectionMultiplexer>();
		multiplexer.GetDatabase().Returns(database);

		return (multiplexer, database);
	}
}
