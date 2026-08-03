using System.Net.WebSockets;
using Chat.Protos;
using Common;
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

	public static void MapGatewayWebSocket(this WebApplication app, string pattern = "/ws")
	{
		app.Map(pattern, async (
			HttpContext context,
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

			using var socket = await context.WebSockets.AcceptWebSocketAsync();
			var connection = await lifecycle.OnConnectedAsync(socket, cancellationToken);

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
				await lifecycle.OnDisconnectedAsync(connection.ConnectionId, cancellationToken);
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

			await inbound.HandleAsync(connection.ConnectionId, packet.Subject, packet.Payload, cancellationToken).ConfigureAwait(false);
		}
	}
}
