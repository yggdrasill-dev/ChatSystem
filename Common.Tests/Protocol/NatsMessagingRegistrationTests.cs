using Adaptare;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Common.Tests.Protocol;

// AddOutboundGateway / AddConnectionTerminator / AddInboundBridge 各自都需要 Adaptare 的
// message queue 設定，而 Gateway 與 Dispatcher 又會為了註冊自己的 handler 再呼叫一次
// AddNatsMessageQueue。這個測試釘住「這些組合都能成功建出 IMessageSender」——
// 共用設定的 marker 只擋得住我們自己的重複呼叫，擋不住應用程式那一次。
public class NatsMessagingRegistrationTests
{
	[Fact]
	public void CommandRouterComposition_ResolvesMessageSender()
	{
		var services = NewServices();

		services.AddOutboundGateway();
		services.AddConnectionTerminator();

		using var provider = services.BuildServiceProvider();

		Assert.NotNull(provider.GetRequiredService<IMessageSender>());
	}

	[Fact]
	public void GatewayComposition_ResolvesBridgeAndSender_WhenTheAppAlsoRegistersItsOwnHandlers()
	{
		var services = NewServices();

		services.AddInboundBridge();

		// Gateway/Program.cs 自己還會再呼叫一次，為了註冊 connect.deliver.{nodeId} 的 handler
		services
			.AddMessageQueue()
			.AddNatsMessageQueue(config => config
				.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()));

		using var provider = services.BuildServiceProvider();

		Assert.NotNull(provider.GetRequiredService<IMessageSender>());
		Assert.NotNull(provider.GetRequiredService<IInboundMessageHandler>());
	}

	private static ServiceCollection NewServices()
	{
		var services = new ServiceCollection();

		services.AddLogging();
		// NatsConnection 的建構子不會連線，這裡只是讓 ConfigureResolveConnection 解得出東西
		services.AddSingleton<INatsConnection>(new NatsConnection());

		return services;
	}
}
