using Common;
using Common.Protocol;
using Google.Protobuf;

namespace Microsoft.Extensions.DependencyInjection;

public static class ProtocolLayerServiceCollectionExtensions
{
	// Gateway 端：取代 NoOpInboundMessageHandler 的註冊。
	// 需要 IMessageSender，也就是宿主自己組的那一次 Adaptare message queue
	// （見 ConnectionLayerServiceCollectionExtensions 的說明）。
	public static IServiceCollection AddInboundBridge(this IServiceCollection services) =>
		services.AddSingleton<IInboundMessageHandler, InboundBridge>();

	// CommandRouter 端：registry 與出口。
	// 需要先呼叫 AddOutboundGateway()，IPacketPublisher 靠它把訊息交給 Dispatcher。
	public static IServiceCollection AddPacketRegistry(this IServiceCollection services)
	{
		services.AddSingleton<PacketRegistry>();

		return services.AddSingleton<IPacketPublisher, PacketPublisher>();
	}

	// 註冊一個 inbound 命令。subject 字面值在整個 codebase 只該出現在這裡一次。
	public static IServiceCollection AddPacketHandler<TMessage, THandler>(this IServiceCollection services, string subject)
		where TMessage : IMessage<TMessage>, new()
		where THandler : class, IPacketHandler<TMessage>
	{
		// protobuf 產生的類別有 static Parser，但泛型約束拿不到它；用 new() 約束自己建一個。
		var parser = new MessageParser<TMessage>(() => new TMessage());

		services.AddScoped<THandler>();

		return services.AddSingleton(new PacketRegistration(
			subject,
			typeof(TMessage),
			(serviceProvider, context, payload, cancellationToken) =>
			{
				// 先解析再取 handler：payload 壞掉時不必白白建出 handler。
				var message = parser.ParseFrom(payload);

				return serviceProvider
					.GetRequiredService<THandler>()
					.HandleAsync(context, message, cancellationToken);
			}));
	}

	// 只出現在下行的訊息型別：宣告 subject 供 IPacketPublisher 反查，沒有 handler。
	// client 送這種 subject 上來會被當成未知 subject（見 PacketRegistry.IsInboundSubject）。
	public static IServiceCollection AddOutboundPacket<TMessage>(this IServiceCollection services, string subject)
		where TMessage : IMessage<TMessage>
		=> services.AddSingleton(new PacketRegistration(subject, typeof(TMessage), null));
}
