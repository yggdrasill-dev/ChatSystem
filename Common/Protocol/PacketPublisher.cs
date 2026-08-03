using Common.Delivery;
using Google.Protobuf;

namespace Common.Protocol;

internal sealed class PacketPublisher(PacketRegistry registry, IOutboundGateway outboundGateway) : IPacketPublisher
{
	public ValueTask PublishAsync<TMessage>(
		IReadOnlyCollection<string> connectionIds,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage>
	{
		var subject = registry.ResolveSubject(typeof(TMessage));

		return outboundGateway.DeliverAsync(subject, connectionIds, message.ToByteString(), cancellationToken);
	}
}
