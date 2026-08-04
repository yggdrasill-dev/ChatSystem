using Common.Rooms;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class RoomLayerServiceCollectionExtensions
{
	// 房間層用自己的 Redis 資源（AppHost 的 room-store），不共用連線層的 connection-directory：
	// 房間資料是持久資料、需要開 AOF/RDB，跟連線層那個純快取用途的設定需求不同，而且
	// connection-layer.md ADR-2 的擁有權原則就是「每一層的基礎設施由自己擁有」。
	//
	// 因為同一個 process（CommandRouter）會同時需要兩個 Redis，這裡必須用 keyed service
	// 取得對應的 client：先呼叫 builder.AddKeyedRedisClient(redisServiceKey)。
	public static IServiceCollection AddRoomStore(this IServiceCollection services, object redisServiceKey)
	{
		services.AddSingleton<IRoomStore>(sp =>
			new RedisRoomStore(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		services.AddSingleton<IRoomBanList>(sp =>
			new RedisRoomBanList(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		// 成員名單是暫時狀態、不是持久資料，但仍然放房間層自己的 Redis：它跟房間資料要
		// 跨 key 一起操作（斷線標記那段 Lua），分開兩個 instance 就辦不到。
		services.TryAddSingleton(TimeProvider.System);

		return services.AddSingleton<IRoomMembership>(sp =>
			new RedisRoomMembership(
				sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey),
				sp.GetRequiredService<TimeProvider>()));
	}
}
