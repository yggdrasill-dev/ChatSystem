using ChatConnector.Protos;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Valhalla.WebSockets;

namespace ChatConnector
{
	public class ClientConnectHandler : IWebSocketConnectionHandler
	{
		private readonly ILogger<ClientConnectHandler> m_Logger;

		public ClientConnectHandler(ILogger<ClientConnectHandler> logger)
		{
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public ValueTask OnCloseAsync(HttpContext httpContext, WebSocket socket, WebSocketCloseStatus? closeStatus, string closeDescription, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} closed!");

			return ValueTask.CompletedTask;
		}

		public ValueTask OnConnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} connected!");

			return ValueTask.CompletedTask;
		}

		public ValueTask OnDisconnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} disconnected!");

			return ValueTask.CompletedTask;
		}

		public ValueTask OnReceiveAsync(HttpContext httpContext, WebSocket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
		{
			var msg = ChatMessage.Parser.ParseFrom(new ReadOnlySequence<byte>(buffer));

			m_Logger.LogInformation($"receive subject: {msg.Subject}, payload: {msg.Payload.ToStringUtf8()}");

			return ValueTask.CompletedTask;
		}
	}
}
