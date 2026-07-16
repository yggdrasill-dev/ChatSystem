using Adaptare;
using Chat.Protos;
using Gateway.Models;

namespace Gateway.Services;

// 訂閱這個節點專屬的投遞 subject（connect.deliver.{nodeId}），把 Dispatcher 分好組的封包送給本地連線。
// 訂閱的生命週期交給 Adaptare.Nats 管理，這裡只處理收到訊息後的行為。
public sealed class DeliverPacketHandler(
	ConnectionRegistry registry,
	ILogger<DeliverPacketHandler> logger)
	: IMessageHandler<byte[]>
{
	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		var packet = DeliverPacket.Parser.ParseFrom(data);

		foreach (var connectionId in packet.ConnectionIds)
		{
			var delivered = await registry
				.TryDeliverAsync(connectionId, packet.Subject, packet.Payload, cancellationToken)
				.ConfigureAwait(false);

			if (!delivered)
				logger.LogDebug("Connection {ConnectionId} not found on this node, skip.", connectionId);
		}
	}
}