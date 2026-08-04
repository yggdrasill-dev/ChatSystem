using Adaptare;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Common.Tests.Protocol;

// 釘住「Adaptare 的 message queue 在一個 host 裡只被註冊一次」。
//
// 這是一個真的踩過的 bug：共用設定原本包在 AddNatsMessaging() 裡並用 marker 擋重複，但
// marker 擋不住「應用程式為了註冊自己的 handler 又呼叫一次 AddNatsMessageQueue」。每多一次
// 就多一個 IMessageQueueBackgroundRegistration，而 handler 設定是共用的 options，結果是
// **每個訂閱被建立兩份、每則下行訊息被投遞兩次**。
//
// 它撐過了兩次端到端驗證，因為在房間層出現之前下行只有 terminate（重複是 no-op）與 ack
// （request/reply 只取第一個回覆）。所以這裡直接數註冊次數，而不是只驗「解得出 IMessageSender」。
public class NatsMessagingRegistrationTests
{
	[Fact]
	public void LayerRegistrations_DoNotSetUpMessagingThemselves()
	{
		var services = NewServices();

		services.AddOutboundGateway();
		services.AddConnectionTerminator();
		services.AddConnectionEventPublisher();
		services.AddInboundBridge();

		// 這四個只註冊自己的服務。messaging 由宿主組一次——包在這裡的那個版本正是 bug 來源。
		Assert.Empty(BackgroundRegistrations(services));
	}

	[Fact]
	public void HostComposition_RegistersExactlyOneBackgroundRegistration()
	{
		var services = NewServices();

		services.AddOutboundGateway();
		services.AddConnectionTerminator();
		services.AddInboundBridge();

		AddHostMessaging(services);

		Assert.Single(BackgroundRegistrations(services));

		using var provider = services.BuildServiceProvider();

		Assert.NotNull(provider.GetRequiredService<IMessageSender>());
		Assert.NotNull(provider.GetRequiredService<IInboundMessageHandler>());
	}

	[Fact]
	public void TwoHostMessagingCalls_WouldDoubleEverySubscription()
	{
		var services = NewServices();

		AddHostMessaging(services);
		AddHostMessaging(services);

		// 這個測試不是在鼓勵這樣寫——它記錄「為什麼那段只能出現一次」。
		// 兩份 background registration = 每個訂閱兩份 = 每則訊息投遞兩次。
		Assert.Equal(2, BackgroundRegistrations(services).Count);
	}

	private static void AddHostMessaging(IServiceCollection services) =>
		services
			.AddMessageQueue()
			.AddNatsGlobPatternExchange("*")
			.AddNatsMessageQueue(config => config
				.ConfigureResolveConnection(sp => (NatsConnection)sp.GetRequiredService<INatsConnection>()));

	private static List<ServiceDescriptor> BackgroundRegistrations(IServiceCollection services) =>
		[.. services.Where(descriptor => descriptor.ServiceType == typeof(IMessageQueueBackgroundRegistration))];

	private static ServiceCollection NewServices()
	{
		var services = new ServiceCollection();

		services.AddLogging();
		// NatsConnection 的建構子不會連線，這裡只是讓 ConfigureResolveConnection 解得出東西
		services.AddSingleton<INatsConnection>(new NatsConnection());

		return services;
	}
}
