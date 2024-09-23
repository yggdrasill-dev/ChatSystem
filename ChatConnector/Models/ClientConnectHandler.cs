using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Valhalla.WebSockets;

namespace ChatConnector.Models;

public class ClientConnectHandler(
	string connectorId,
	ILogger<ClientConnectHandler> logger) : IWebSocketConnectionHandler
{
	public ValueTask OnCloseAsync(
		HttpContext httpContext,
		WebSocket socket,
		WebSocketCloseStatus? closeStatus,
		string closeDescription,
		CancellationToken cancellationToken)
	{
		logger.LogInformation("{TraceIdentifier} closed!", httpContext.TraceIdentifier);

		return ValueTask.CompletedTask;
	}

	public async ValueTask OnConnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
	{
		logger.LogInformation("{TraceIdentifier} connected!", httpContext.TraceIdentifier);
		using var scope = httpContext.RequestServices.CreateScope();

		if (!httpContext.User.Identity.IsAuthenticated)
		{
			var rejectMsg = new LoginReply()
			{
				Status = LoginStatus.Reject
			};

			var reply = new Packet
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

			await regCommandService.ExecuteAsync(new RegisterSessionCommand
			{
				SessionId = httpContext.TraceIdentifier,
				ConnectorId = connectorId,
				Name = httpContext.User.Identity.Name
			});

			await addSocketCommandService.ExecuteAsync(new AddSocketCommand
			{
				SessionId = httpContext.TraceIdentifier,
				Socket = socket
			});

			var accpetMsg = new LoginReply
			{
				Status = LoginStatus.Accpet,
				Name = httpContext.User.Identity.Name
			};

			var reply = new Packet
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

	public async ValueTask OnDisconnectedAsync(HttpContext httpContext, WebSocket socket, CancellationToken cancellationToken)
	{
		using var scope = httpContext.RequestServices.CreateScope();

		logger.LogInformation("{TraceIdentifier} disconnected!", httpContext.TraceIdentifier);

		var removeSocketService = scope.ServiceProvider.GetRequiredService<ICommandService<RemoveSocketCommand>>();
		var leaveRoomService = scope.ServiceProvider.GetRequiredService<ICommandService<LeaveRoomCommand>>();
		var unregisterService = scope.ServiceProvider.GetRequiredService<ICommandService<UnregisterSessionCommand>>();

		await leaveRoomService.ExecuteAsync(new LeaveRoomCommand
		{
			SessionId = httpContext.TraceIdentifier
		}).ConfigureAwait(false);

		await unregisterService.ExecuteAsync(new UnregisterSessionCommand
		{
			SessionId = httpContext.TraceIdentifier
		}).ConfigureAwait(false);

		await removeSocketService.ExecuteAsync(new RemoveSocketCommand
		{
			SessionId = httpContext.TraceIdentifier
		}).ConfigureAwait(false);
	}

	public ValueTask OnReceiveAsync(HttpContext httpContext, WebSocket socket, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
	{
		var scope = httpContext.RequestServices.CreateScope();

		var msg = Packet.Parser.ParseFrom(new ReadOnlySequence<byte>(buffer));

		logger.LogInformation("receive subject: {Subject}", msg.Subject);

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
