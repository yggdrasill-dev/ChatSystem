using Common.Connections;
using Common.Delivery;
using NATS.Client.Core;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConnectionLayerServiceCollectionExtensions
{
	// 需要先呼叫 builder.AddRedisClient(...) 註冊 IConnectionMultiplexer。
	public static IServiceCollection AddConnectionDirectory(this IServiceCollection services) =>
		services.AddSingleton<IConnectionDirectory, RedisConnectionDirectory>();

	// 需要先呼叫 builder.AddNatsClient(...) 註冊 INatsConnection。
	public static IServiceCollection AddOutboundGateway(this IServiceCollection services)
	{
		services.AddConnectionLayerMessaging();

		return services.AddSingleton<IOutboundGateway, OutboundGateway>();
	}

	// 需要先呼叫 builder.AddNatsClient(...) 註冊 INatsConnection。
	public static IServiceCollection AddConnectionTerminator(this IServiceCollection services)
	{
		services.AddConnectionLayerMessaging();

		return services.AddSingleton<IConnectionTerminator, ConnectionTerminator>();
	}

	// OutboundGateway 與 ConnectionTerminator 共用的 Adaptare 設定。
	// 兩者常常會被同時註冊（例如未來的協定層同時要投遞與終止連線），
	// 用 marker 確保這組設定只跑一次。
	private static void AddConnectionLayerMessaging(this IServiceCollection services)
	{
		if (services.Any(descriptor => descriptor.ServiceType == typeof(ConnectionLayerMessagingMarker)))
			return;

		services.AddSingleton<ConnectionLayerMessagingMarker>();

		services
			.AddMessageQueue()
			.AddNatsMessageQueue(config => config
				.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()))
			.AddNatsGlobPatternExchange("*");
	}

	private sealed class ConnectionLayerMessagingMarker;
}
