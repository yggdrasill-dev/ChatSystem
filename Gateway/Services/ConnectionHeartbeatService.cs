using Common.Connections;
using Gateway.Models;

namespace Gateway.Services;

// ConnectionDirectory 的項目有 30 秒 TTL（存活容錯用），定期續命避免閒置連線被判定過期。
public sealed class ConnectionHeartbeatService(
	GatewayNodeId nodeId,
	ConnectionRegistry registry,
	IConnectionDirectory connectionDirectory,
	ILogger<ConnectionHeartbeatService> logger) 
	: BackgroundService
{
	private static readonly TimeSpan _Interval = TimeSpan.FromSeconds(10);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_Interval);

		while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
		{
			var connectionIds = registry.ConnectionIds;

			foreach (var connectionId in connectionIds)
			{
				try
				{
					await connectionDirectory.RegisterAsync(connectionId, nodeId.Value, stoppingToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "Failed to refresh heartbeat for {ConnectionId}.", connectionId);
				}
			}
		}
	}
}
