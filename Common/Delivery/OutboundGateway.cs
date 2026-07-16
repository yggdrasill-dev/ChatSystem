using Adaptare;
using Chat.Protos;
using Google.Protobuf;

namespace Common.Delivery;

internal sealed class OutboundGateway(IMessageSender messageSender) : IOutboundGateway
{
	private const string DispatchSubject = "dispatch.deliver";

	public ValueTask DeliverAsync(
		string subject,
		IReadOnlyCollection<string> connectionIds,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		var request = new DeliverRequest { Subject = subject, Payload = payload };
		request.ConnectionIds.AddRange(connectionIds);

		return messageSender.PublishAsync(DispatchSubject, request.ToByteArray(), cancellationToken);
	}
}
