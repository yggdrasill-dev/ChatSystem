using Chat.Protos;
using Gateway.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Gateway.Services;

// 訂閱這個節點專屬的投遞 subject，把 Dispatcher 分好組的封包送給本地連線。
public sealed class DeliveryReceiverService(
	GatewayNodeId nodeId,
	INatsConnection connection,
	ConnectionRegistry registry,
	ILogger<DeliveryReceiverService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var subject = $"connect.deliver.{nodeId.Value}";

		logger.LogInformation("Subscribing to {Subject}.", subject);

		await foreach (var msg in connection.SubscribeAsync<byte[]>(subject, cancellationToken: stoppingToken))
		{
			try
			{
				var packet = DeliverPacket.Parser.ParseFrom(msg.Data);

				foreach (var connectionId in packet.ConnectionIds)
				{
					var delivered = await registry
						.TryDeliverAsync(connectionId, packet.Subject, packet.Payload, stoppingToken)
						.ConfigureAwait(false);

					if (!delivered)
						logger.LogDebug("Connection {ConnectionId} not found on this node, skip.", connectionId);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to handle delivery on {Subject}.", subject);
			}
		}
	}
}
