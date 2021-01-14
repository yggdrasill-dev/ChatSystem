using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using ChatConnector.Models;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valhalla.WebSockets;

namespace ChatConnector
{
	public class ClientConnectHandler : IWebSocketConnectionHandler
	{
		private readonly ILogger<ClientConnectHandler> m_Logger;
		private readonly Guid m_ConnectorId = Guid.NewGuid();

		public ClientConnectHandler(
			ILogger<ClientConnectHandler> logger)
		{
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public ValueTask OnCloseAsync(HttpContext httpContext, WebSocket socket, WebSocketCloseStatus? closeStatus, string closeDescription, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} closed!");

			return ValueTask.CompletedTask;
		}

		public async ValueTask OnConnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} connected!");
			using var scope = httpContext.RequestServices.CreateScope();

			if (!httpContext.User.Identity.IsAuthenticated)
			{
				var rejectMsg = new LoginReply()
				{
					Status = LoginStatus.Reject
				};

				var reply = new ChatMessage
				{
					Subject = "connect.login.reply",
					Payload = rejectMsg.ToByteString()
				};

				await socket.SendAsync(
					reply.ToByteArray(),
					WebSocketMessageType.Binary,
					true,
					cancellationToken).ConfigureAwait(false);

				await socket.CloseOutputAsync(
					WebSocketCloseStatus.PolicyViolation,
					null,
					cancellationToken).ConfigureAwait(false);
			}
			else
			{
				var regCommandService = scope.ServiceProvider.GetRequiredService<ICommandService<RegisterSessionCommand>>();
				var addSocketCommandService = scope.ServiceProvider.GetRequiredService<ICommandService<AddSocketCommand>>();
				var joinRoomCommandService = scope.ServiceProvider.GetRequiredService<ICommandService<JoinRoomCommand>>();

				await regCommandService.ExecuteAsync(new RegisterSessionCommand
				{
					SessionId = httpContext.TraceIdentifier,
					ConnectorId = m_ConnectorId.ToString("N"),
					Name = httpContext.User.Identity.Name
				});

				await joinRoomCommandService.ExecuteAsync(new JoinRoomCommand
				{
					SessionId = httpContext.TraceIdentifier,
					Room = "test"
				});

				await addSocketCommandService.ExecuteAsync(new AddSocketCommand
				{
					SessionId = httpContext.TraceIdentifier,
					Socket = socket
				});

				var accpetMsg = new LoginReply
				{
					Status = LoginStatus.Accpet,
					Name = httpContext.User.Identity.Name,
					Room = "test"
				};

				var reply = new ChatMessage
				{
					Subject = "connect.login.reply",
					Payload = accpetMsg.ToByteString()
				};

				await socket.SendAsync(
					reply.ToByteArray(),
					WebSocketMessageType.Binary,
					true,
					cancellationToken).ConfigureAwait(false);
			}
		}

		public ValueTask OnDisconnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
		{
			m_Logger.LogInformation($"{httpContext.TraceIdentifier} disconnected!");

			return ValueTask.CompletedTask;
		}

		public ValueTask OnReceiveAsync(HttpContext httpContext, WebSocket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
		{
			var scope = httpContext.RequestServices.CreateScope();

			var msg = ChatMessage.Parser.ParseFrom(new ReadOnlySequence<byte>(buffer));

			m_Logger.LogInformation($"receive subject: {msg.Subject}");

			var sendCommand = new SendQueueCommand
			{
				Subject = msg.Subject,
				SessionId = httpContext.TraceIdentifier,
				Payload = msg.Payload
			};
			var sendCommandService = scope.ServiceProvider.GetRequiredService<ICommandService<SendQueueCommand>>();

			return sendCommandService.ExecuteAsync(sendCommand);
		}
	}
}
