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
		services
			.AddMessageQueue()
			.AddNatsMessageQueue(config => config
				.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()))
			.AddNatsGlobPatternExchange("*");

		return services.AddSingleton<IOutboundGateway, OutboundGateway>();
	}
}
