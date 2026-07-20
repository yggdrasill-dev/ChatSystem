using Adaptare;
using Chat.Protos;
using Google.Protobuf;

namespace Common.Delivery;

internal sealed class OutboundGateway(IMessageSender messageSender) : IOutboundGateway
{
	private const string DispatchSubject = "dispatch.deliver";

	public async ValueTask DeliverAsync(
		string subject,
		IReadOnlyCollection<string> connectionIds,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		foreach (var batch in DeliveryBatching.Chunk(connectionIds, payload.Length))
		{
			var request = new DeliverRequest { Subject = subject, Payload = payload };
			request.ConnectionIds.AddRange(batch);

			await messageSender.PublishAsync(DispatchSubject, request.ToByteArray(), cancellationToken).ConfigureAwait(false);
		}
	}
}
