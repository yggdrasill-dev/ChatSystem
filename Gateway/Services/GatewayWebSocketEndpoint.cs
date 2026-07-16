using System.Net.WebSockets;
using Chat.Protos;
using Common;
using Gateway.Models;

namespace Gateway.Services;

public static class GatewayWebSocketEndpoint
{
	private const int ReceiveBufferSize = 8192;

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
				await ReceiveLoopAsync(connection, inbound, cancellationToken);
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

				stream.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);

			stream.Position = 0;

			var packet = Packet.Parser.ParseFrom(stream);

			await inbound.HandleAsync(connection.ConnectionId, packet.Subject, packet.Payload, cancellationToken).ConfigureAwait(false);
		}
	}
}
