using Common;
using Common.Connections;
using Common.Delivery;

namespace Microsoft.Extensions.DependencyInjection;

public static class ConnectionLayerServiceCollectionExtensions
{
	// 需要先呼叫 builder.AddRedisClient(...) 註冊 IConnectionMultiplexer。
	public static IServiceCollection AddConnectionDirectory(this IServiceCollection services) =>
		services.AddSingleton<IConnectionDirectory, RedisConnectionDirectory>();

	// 以下三個都需要 IMessageSender，也就是需要宿主自己組一次 Adaptare 的 message queue：
	//
	//   builder.Services
	//       .AddMessageQueue()
	//       .AddNatsGlobPatternExchange("*")
	//       .AddNatsMessageQueue(config => config
	//           .ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>())
	//           ...自己的 handler...);
	//
	// **那一段在每個 host 只能出現一次**，理由見 Common/NatsMessagingRegistration.cs。
	// 這裡刻意不幫忙包起來——包起來的那個版本正是重複註冊 bug 的來源。
	public static IServiceCollection AddOutboundGateway(this IServiceCollection services) =>
		services.AddSingleton<IOutboundGateway, OutboundGateway>();

	public static IServiceCollection AddConnectionTerminator(this IServiceCollection services) =>
		services.AddSingleton<IConnectionTerminator, ConnectionTerminator>();

	public static IServiceCollection AddConnectionEventPublisher(this IServiceCollection services) =>
		services.AddSingleton<IConnectionEventPublisher, ConnectionEventPublisher>();
}
