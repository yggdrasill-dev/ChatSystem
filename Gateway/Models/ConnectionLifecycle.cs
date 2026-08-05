using System.Net.WebSockets;
using Common.Connections;
using Common.Identity;

namespace Gateway.Models;

// 聚合 OnConnect / OnDisconnect 該發生的步驟順序，取代舊實作裡手動串接多個 Command 的寫法。
//
// 身分相關的動作全部委派給 IConnectionAuthenticator（身分層擁有）：這裡只知道「連線建立後
// 要綁定、關閉後要解綁」，不知道 Supersede 這條規則存在、也不知道 principal 代表什麼。
// 跟舊 ClientConnectHandler 的差異不在「有沒有碰身分」而在碰的方式——那邊是直接認識
// SessionServer 與房間並呼叫它們的 Command。
public sealed class ConnectionLifecycle(
	GatewayNodeId nodeId,
	ConnectionRegistry registry,
	IConnectionDirectory connectionDirectory,
	IConnectionEventPublisher connectionEventPublisher,
	IConnectionAuthenticator connectionAuthenticator,
	ILogger<ConnectionLifecycle> logger)
{
	public async ValueTask<Connection> OnConnectedAsync(
		WebSocket socket,
		string principal,
		CancellationToken cancellationToken = default)
	{
		var connection = new Connection(Guid.NewGuid().ToString("N"), principal, socket);

		registry.Add(connection);
		await connectionDirectory.RegisterAsync(connection.ConnectionId, nodeId.Value, cancellationToken).ConfigureAwait(false);

		// 綁定只能發生在 ConnectionId 產生之後，也就是 socket 已經被 accept 之後。因此
		// 「已 accept、尚未綁定」有一個幾毫秒的窗口，這段時間 fan-out 還找不到這條連線；
		// 後果是剛連上的瞬間可能漏收一則廣播，刻意不處理（重連後靠聊天層歷史記錄補齊）。
		await connectionAuthenticator
			.BindConnectionAsync(principal, connection.ConnectionId, cancellationToken)
			.ConfigureAwait(false);

		return connection;
	}

	public async ValueTask OnDisconnectedAsync(
		string connectionId,
		string principal,
		CancellationToken cancellationToken = default)
	{
		registry.Remove(connectionId);
		await connectionDirectory.UnregisterAsync(connectionId, cancellationToken).ConfigureAwait(false);

		// 解綁帶著 connectionId，身分層只在「目前綁的就是這條連線」時才真的清掉——
		// Supersede 時新連線已經先綁好了，這裡不能把它蓋掉。
		await connectionAuthenticator
			.UnbindConnectionAsync(principal, connectionId, cancellationToken)
			.ConfigureAwait(false);

		// 事件放在清理之後才發：訂閱端收到時這條連線已經不在 ConnectionDirectory 上，
		// 不會出現「收到斷線通知卻還查得到節點」這種自相矛盾的中間狀態。
		try
		{
			await connectionEventPublisher
				.PublishDisconnectedAsync(connectionId, nodeId.Value, principal, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// 呼叫端刻意不再傳請求的 token 進來（見 GatewayWebSocketEndpoint 的 finally），
			// 所以走到這裡代表清理自己的期限到了——訂閱端不會知道這條連線斷了，是真的異常狀況。
			//
			// 這一行原本記 Debug，理由寫的是「關站時每條連線都會走到這裡」。結果它把一個
			// 「事件從來沒發出去」的 bug 藏了整整兩輪端到端驗證。
			logger.LogWarning("Disconnected event for {ConnectionId} timed out before it was published.", connectionId);
		}
		catch (Exception ex)
		{
			// 發不出去不能影響清理本身。事件語意是 best-effort，訂閱端本來就必須容忍漏送
			// （見 connection-layer.md ADR-8）。
			logger.LogError(ex, "Failed to publish the disconnected event for {ConnectionId}.", connectionId);
		}
	}
}
