using Chat.Protos;
using Google.Protobuf;
using NATS.Client.Core;

namespace Common.Delivery;

internal sealed class OutboundGateway(INatsConnection connection) : IOutboundGateway
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

		return connection.PublishAsync(DispatchSubject, request.ToByteArray(), cancellationToken: cancellationToken);
	}
}
