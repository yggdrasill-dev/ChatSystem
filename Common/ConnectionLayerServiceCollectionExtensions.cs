using Common;
using Common.Connections;
using Common.Delivery;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConnectionLayerServiceCollectionExtensions
{
	// 需要先呼叫 builder.AddRedisClient(...) 註冊 IConnectionMultiplexer。
	public static IServiceCollection AddConnectionDirectory(this IServiceCollection services) =>
		services.AddSingleton<IConnectionDirectory, RedisConnectionDirectory>();

	// 需要先呼叫 builder.AddNatsClient(...) 註冊 INatsConnection。
	public static IServiceCollection AddOutboundGateway(this IServiceCollection services)
	{
		services.AddNatsMessaging();

		return services.AddSingleton<IOutboundGateway, OutboundGateway>();
	}

	// 需要先呼叫 builder.AddNatsClient(...) 註冊 INatsConnection。
	public static IServiceCollection AddConnectionTerminator(this IServiceCollection services)
	{
		services.AddNatsMessaging();

		return services.AddSingleton<IConnectionTerminator, ConnectionTerminator>();
	}

	// 需要先呼叫 builder.AddNatsClient(...) 註冊 INatsConnection。
	public static IServiceCollection AddConnectionEventPublisher(this IServiceCollection services)
	{
		services.AddNatsMessaging();

		return services.AddSingleton<IConnectionEventPublisher, ConnectionEventPublisher>();
	}
}
