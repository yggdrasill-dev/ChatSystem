using System.Net.WebSockets;
using Common.Connections;

namespace Gateway.Models;

// 聚合 OnConnect / OnDisconnect 該發生的步驟順序，取代舊實作裡手動串接多個 Command 的寫法。
// 刻意不含任何身分驗證、不呼叫任何「註冊使用者」的動作——那些是使用者管理層的事。
public sealed class ConnectionLifecycle(
	GatewayNodeId nodeId,
	ConnectionRegistry registry,
	IConnectionDirectory connectionDirectory,
	IConnectionEventPublisher connectionEventPublisher,
	ILogger<ConnectionLifecycle> logger)
{
	public async ValueTask<Connection> OnConnectedAsync(WebSocket socket, CancellationToken cancellationToken = default)
	{
		var connection = new Connection(Guid.NewGuid().ToString("N"), socket);

		registry.Add(connection);
		await connectionDirectory.RegisterAsync(connection.ConnectionId, nodeId.Value, cancellationToken).ConfigureAwait(false);

		return connection;
	}

	public async ValueTask OnDisconnectedAsync(string connectionId, CancellationToken cancellationToken = default)
	{
		registry.Remove(connectionId);
		await connectionDirectory.UnregisterAsync(connectionId, cancellationToken).ConfigureAwait(false);

		// 事件放在清理之後才發：訂閱端收到時這條連線已經不在 ConnectionDirectory 上，
		// 不會出現「收到斷線通知卻還查得到節點」這種自相矛盾的中間狀態。
		try
		{
			await connectionEventPublisher
				.PublishDisconnectedAsync(connectionId, nodeId.Value, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// 關站時每條連線都會走到這裡，不是異常狀況，不能用 Error 把 log 洗掉。
			logger.LogDebug("Disconnected event for {ConnectionId} was cancelled, likely shutdown.", connectionId);
		}
		catch (Exception ex)
		{
			// 發不出去不能影響清理本身。事件語意是 best-effort，訂閱端本來就必須容忍漏送
			// （見 connection-layer.md ADR-8）。
			logger.LogError(ex, "Failed to publish the disconnected event for {ConnectionId}.", connectionId);
		}
	}
}
