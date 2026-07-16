using System.Net.WebSockets;
using Common.Connections;

namespace Gateway.Models;

// 聚合 OnConnect / OnDisconnect 該發生的步驟順序，取代舊實作裡手動串接多個 Command 的寫法。
// 刻意不含任何身分驗證、不呼叫任何「註冊使用者」的動作——那些是使用者管理層的事。
public sealed class ConnectionLifecycle(
	GatewayNodeId nodeId,
	ConnectionRegistry registry,
	IConnectionDirectory connectionDirectory)
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
	}
}
