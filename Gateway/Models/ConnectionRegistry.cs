using System.Collections.Concurrent;
using System.Net.WebSockets;
using Google.Protobuf;

namespace Gateway.Models;

// 單一 Gateway 節點內的本地連線表：ConnectionId -> Connection。
// 取代舊實作的 WebSocketRepository（貧血的 ConcurrentDictionary 子類別）。
public sealed class ConnectionRegistry
{
	// 刻意用中性字串：連線層不知道上層為什麼要關這條連線（Supersede？違反協定？），
	// 不能在這裡寫死任何上層語意。要讓 client 分辨原因的話得由上層另外送訊息說明。
	private const string CloseDescription = "Connection terminated by server.";

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

	// 上層透過 IConnectionTerminator 要求關閉某條連線時走這裡。
	// 查不到就回 false 不報錯，沿用 ResolveNodesAsync 的既有慣例——呼叫端不需要處理
	// 「這條連線其實已經自己斷了」這種情況。
	public async ValueTask<bool> TryCloseAsync(string connectionId, CancellationToken cancellationToken = default)
	{
		if (!m_Connections.TryGetValue(connectionId, out var connection))
			return false;

		try
		{
			await connection
				.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, CloseDescription, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException)
		{
			// 跟「連線正在自然斷開」競爭時會走到這裡：socket 已經被關掉/釋放，
			// 但 receive loop 的 finally 還沒把它從表上移除。結果跟查不到一樣。
			return false;
		}

		// 這裡刻意不呼叫 Remove：socket 關閉後 receive loop 會自然結束，
		// 由 GatewayWebSocketEndpoint 的 finally 走既有的 OnDisconnectedAsync 收尾。
		return true;
	}
}
