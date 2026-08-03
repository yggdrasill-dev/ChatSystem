using Adaptare;
using Chat.Protos;
using Gateway.Models;

namespace Gateway.Services;

// 訂閱這個節點專屬的終止 subject（connect.terminate.{nodeId}），把 Dispatcher 分好組的
// connectionId 逐一關掉。跟 DeliverPacketHandler 對稱，只是動作從「送封包」換成「關連線」。
public sealed class TerminatePacketHandler(
	ConnectionRegistry registry,
	ILogger<TerminatePacketHandler> logger)
	: IMessageHandler<byte[]>
{
	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		var packet = TerminatePacket.Parser.ParseFrom(data);

		foreach (var connectionId in packet.ConnectionIds)
		{
			var closed = await registry.TryCloseAsync(connectionId, cancellationToken).ConfigureAwait(false);

			if (!closed)
				logger.LogDebug("Connection {ConnectionId} not found on this node, skip.", connectionId);
		}
	}
}
