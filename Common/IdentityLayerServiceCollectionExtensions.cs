using Common.Identity;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class IdentityLayerServiceCollectionExtensions
{
	// 身分層用自己的 Redis 資源（AppHost 的 identity-store），不共用連線層的
	// connection-directory：Session 是持久資料、需要開 AOF/RDB，跟連線層那個純快取用途的
	// 設定需求不同，而且 connection-layer.md ADR-2 的擁有權原則就是「每一層的基礎設施由
	// 自己擁有」。
	//
	// 因為同一個 process 會同時需要多個 Redis（Gateway 要 connection-directory +
	// identity-store，CommandRouter 還要再加 room-store），這裡必須用 keyed service
	// 取得對應的 client：先呼叫 builder.AddKeyedRedisClient(redisServiceKey)。
	public static IServiceCollection AddIdentityStores(this IServiceCollection services, object redisServiceKey)
	{
		services.AddSingleton<ISessionStore>(sp =>
			new RedisSessionStore(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		services.AddSingleton<ILoginNonceStore>(sp =>
			new RedisLoginNonceStore(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		services.AddSingleton<IUserProfileStore>(sp =>
			new RedisUserProfileStore(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		return services.AddSingleton<IPresenceDirectory>(sp =>
			new RedisPresenceDirectory(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));
	}

	// 連線層在 handshake 呼叫的 hook。需要先呼叫 AddIdentityStores(...) 與
	// AddConnectionTerminator()——後者是 Supersede 踢掉舊連線用的，會 publish 到
	// dispatch.terminate，所以呼叫端也需要 NATS。
	public static IServiceCollection AddConnectionAuthenticator(this IServiceCollection services) =>
		services.AddSingleton<IConnectionAuthenticator, ConnectionAuthenticator>();
}
