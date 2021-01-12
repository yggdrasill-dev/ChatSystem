using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NATS.Client;
using Valhalla.WebSockets;

namespace ChatConnector
{
	public class ClientConnectHandler : IWebSocketConnectionHandler
	{
		private readonly IConnection m_Connection;
		private readonly ILogger<ClientConnectHandler> m_Logger;

		public ClientConnectHandler(ConnectionFactory connectionFactory, ILogger<ClientConnectHandler> logger)
		{
			m_Connection = connectionFactory.CreateConnection();
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

		public async ValueTask OnReceiveAsync(HttpContext httpContext, WebSocket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
		{
			var msg = ChatMessage.Parser.ParseFrom(new ReadOnlySequence<byte>(buffer));

			m_Logger.LogInformation($"receive subject: {msg.Subject}");

			if (msg.Subject == "connect.register")
			{
				var loginInfo = LoginRegistration.Parser.ParseFrom(msg.Payload);

				var registration = new PlayerRegistration
				{
					SessionId = httpContext.TraceIdentifier,
					Name = loginInfo.Name
				};
				await m_Connection.RequestAsync("session.register", registration.ToByteArray()).ConfigureAwait(false);
			}
			else
			{
				var packet = new QueuePacket
				{
					SessionId = httpContext.TraceIdentifier,
					Payload = msg.Payload
				};

				m_Connection.Publish(msg.Subject, packet.ToByteArray());
			}

			//var replyMsg = new ChatMessage
			//{
			//	Subject = "chat.reply",
			//	Payload = ByteString.CopyFromUtf8("Hello")
			//};

			//await socket.SendAsync(
			//	replyMsg.ToByteArray(),
			//	WebSocketMessageType.Binary,
			//	true,
			//	cancellationToken).ConfigureAwait(false);
		}
	}
}
