using System.Collections.Concurrent;
using Google.Protobuf;

namespace Gateway.Models;

// 單一 Gateway 節點內的本地連線表：ConnectionId -> Connection。
// 取代舊實作的 WebSocketRepository（貧血的 ConcurrentDictionary 子類別）。
public sealed class ConnectionRegistry
{
	private readonly ConcurrentDictionary<string, Connection> m_Connections = new();

	public int Count => m_Connections.Count;

	public IReadOnlyCollection<string> ConnectionIds => m_Connections.Keys.ToArray();

	public void Add(Connection connection) => m_Connections[connection.ConnectionId] = connection;

	public void Remove(string connectionId) => m_Connections.TryRemove(connectionId, out _);

	public async ValueTask<bool> TryDeliverAsync(
		string connectionId,
		string subject,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		if (!m_Connections.TryGetValue(connectionId, out var connection))
			return false;

		await connection.SendAsync(subject, payload, cancellationToken).ConfigureAwait(false);

		return true;
	}
}
