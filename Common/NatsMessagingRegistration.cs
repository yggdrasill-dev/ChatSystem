using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;

namespace Common;

// OutboundGateway / ConnectionTerminator / InboundBridge 共用的 Adaptare 設定。
// 這些元件會被同時註冊（CommandRouter 需要前兩個、Gateway 需要第三個），
// 用 marker 確保這組設定只跑一次。
internal static class NatsMessagingRegistration
{
	internal static void AddNatsMessaging(this IServiceCollection services)
	{
		if (services.Any(descriptor => descriptor.ServiceType == typeof(Marker)))
			return;

		services.AddSingleton<Marker>();

		services
			.AddMessageQueue()
			.AddNatsMessageQueue(config => config
				.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()))
			.AddNatsGlobPatternExchange("*");
	}

	private sealed class Marker;
}
