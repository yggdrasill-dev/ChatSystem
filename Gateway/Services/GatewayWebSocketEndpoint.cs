using System.Net.WebSockets;
using Chat.Protos;
using Common;
using Common.Identity;
using Gateway.Models;

namespace Gateway.Services;

public static class GatewayWebSocketEndpoint
{
	private const int ReceiveBufferSize = 8192;

	// 單一 inbound 訊息的大小上限。兩個理由都不是可選的：
	// 1. 分片訊息沒有上限的話，client 送一個永不結束的分片就能把這個節點的記憶體吃光。
	// 2. payload 之後會被上層 publish 到 NATS（預設 max_payload 1MB），
	//    收得下也送不出去，不如在這裡就拒絕。
	private const int MaxMessageBytes = 256 * 1024;

	// 斷線清理自己的期限，跟請求的存活無關——理由見下面 finally 的註解。
	private static readonly TimeSpan _CleanupTimeout = TimeSpan.FromSeconds(5);

	public static void MapGatewayWebSocket(this WebApplication app, string pattern = "/ws")
	{
		app.Map(pattern, async (
			HttpContext context,
			AllowedOrigins allowedOrigins,
			IConnectionAuthenticator authenticator,
			ConnectionLifecycle lifecycle,
			IInboundMessageHandler inbound,
			ILogger<Connection> logger,
			CancellationToken cancellationToken) =>
		{
			if (!context.WebSockets.IsWebSocketRequest)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			// WebSocket handshake 不受 CORS 約束，所以 Origin 必須自己驗，否則任何網站都能
			// 帶著受害者的 cookie 開一條連線（CSWSH）。見 AllowedOrigins。
			var origin = context.Request.Headers.Origin.ToString();

			if (!allowedOrigins.IsAllowed(origin))
			{
				logger.LogWarning("Rejected a websocket handshake from origin '{Origin}'.", origin);

				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return;
			}

			// 驗證必須在 accept 之前——一旦 accept 就沒辦法回 HTTP 狀態碼了，client 只會看到
			// 「連上又立刻被踢」而分不出是 session 過期還是網路問題。
			var principal = await authenticator
				.ResolveAsync(context.Request.Cookies[SessionCookie.Name], cancellationToken)
				.ConfigureAwait(false);

			if (principal is null)
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			using var socket = await context.WebSockets.AcceptWebSocketAsync();
			var connection = await lifecycle.OnConnectedAsync(socket, principal, cancellationToken);

			logger.LogInformation("{ConnectionId} connected.", connection.ConnectionId);

			try
			{
				await ReceiveLoopAsync(connection, inbound, logger, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// 正常關站流程，不視為錯誤
			}
			catch (WebSocketException ex)
			{
				logger.LogInformation(ex, "{ConnectionId} closed unexpectedly.", connection.ConnectionId);
			}
			finally
			{
				// **不能用請求的 cancellationToken**：它就是 HttpContext.RequestAborted，而 client
				// 一斷線（關分頁、網路斷、行程被殺）它就已經被取消了。拿它去做斷線清理等於
				// 「因為連線斷了，所以不清理連線」。真實症狀是 events.connection.disconnected
				// 從來沒被送出去，房間層的寬限期整組是死的——而且完全沒有 log。
				//
				// 給清理一個自己的期限而不是 CancellationToken.None：關站時所有連線同時收尾，
				// 沒有上界會把 shutdown 拖住。
				using var cleanup = new CancellationTokenSource(_CleanupTimeout);

				await lifecycle.OnDisconnectedAsync(connection.ConnectionId, connection.Principal, cleanup.Token);
				logger.LogInformation("{ConnectionId} disconnected.", connection.ConnectionId);
			}
		});
	}

	private static async Task ReceiveLoopAsync(
		Connection connection,
		IInboundMessageHandler inbound,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		var buffer = new byte[ReceiveBufferSize];

		while (connection.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			using var stream = new MemoryStream();
			WebSocketReceiveResult result;

			do
			{
				result = await connection.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					// 收到對方的 close frame 後要送出自己的 close frame 完成 handshake，
					// 否則對方呼叫 CloseAsync 等待回應時會拿到 WebSocketException。
					await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken).ConfigureAwait(false);
					return;
				}

				if (stream.Length + result.Count > MaxMessageBytes)
				{
					logger.LogWarning(
						"{ConnectionId} exceeded the {MaxMessageBytes} byte inbound message limit, closing.",
						connection.ConnectionId,
						MaxMessageBytes);

					// 用 CloseOutputAsync 而非 CloseAsync：這裡不等對方回應，直接結束迴圈讓
					// finally 收尾。剩下的分片不再讀，socket 釋放時一併中止。
					await connection
						.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, null, cancellationToken)
						.ConfigureAwait(false);

					return;
				}

				stream.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);

			stream.Position = 0;

			var packet = Packet.Parser.ParseFrom(stream);

			// principal 從連線本身取，不是每則訊息重查一次——這是把它存在 Connection 上的
			// 全部理由。
			await inbound
				.HandleAsync(connection.ConnectionId, connection.Principal, packet.Subject, packet.Payload, cancellationToken)
				.ConfigureAwait(false);
		}
	}
}
