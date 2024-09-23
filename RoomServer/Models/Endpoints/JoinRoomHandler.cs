using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Endpoints;

public class JoinRoomHandler(
	IMessageQueueService messageQueueService,
	ICommandService<JoinRoomCommand> joinRoomService,
	IQueryService<RoomSessionsQuery, string> listRoomSessionsService,
	ILogger<JoinRoomHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var packet = QueuePacket.Parser.ParseFrom(msg.Data);

		var joinRoomData = JoinRoom.Parser.ParseFrom(packet.Payload);

		var roomSessionIds = listRoomSessionsService
			.QueryAsync(new RoomSessionsQuery
			{
				Room = joinRoomData.Room
			})
			.ToEnumerable()
			.ToArray();

		try
		{
			await joinRoomService.ExecuteAsync(new JoinRoomCommand
			{
				SessionId = packet.SessionId,
				Room = joinRoomData.Room,
				Password = joinRoomData.Password
			}).ConfigureAwait(false);

			var response = new SendPacket
			{
				Subject = "room.join.reply"
			};

			response.SessionIds.Add(packet.SessionId);

			var responseContent = new JoinRoomReply
			{
				Status = JoinRoomStatus.Accpet
			};

			response.Payload = responseContent.ToByteString();

			await messageQueueService.PublishAsync(
				"connect.send",
				response.ToByteArray()).ConfigureAwait(false);

			if (roomSessionIds.Length > 0)
			{
				var broadcast = new SendPacket
				{
					Subject = "chat.receive"
				};

				broadcast.SessionIds.AddRange(roomSessionIds);

				var chatMessage = new ChatMessage
				{
					Scope = Scope.System,
					From = "System",
					Message = $"New player joined!"
				};

				broadcast.Payload = chatMessage.ToByteString();

				await messageQueueService.PublishAsync(
					"connect.send",
					broadcast.ToByteArray()).ConfigureAwait(false);

				var refrashPacket = new SendPacket
				{
					Subject = "room.player.refrash"
				};

				refrashPacket.SessionIds.AddRange(roomSessionIds);

				await messageQueueService.PublishAsync(
					"connect.send",
					refrashPacket.ToByteArray()).ConfigureAwait(false);
			}

			logger.LogInformation("({SessionId}, {Room}) joined.", packet.SessionId, joinRoomData.Room);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "");

			var response = new SendPacket
			{
				Subject = "room.join.reply"
			};

			response.SessionIds.Add(packet.SessionId);

			var responseContent = new JoinRoomReply
			{
				Status = JoinRoomStatus.Reject,
				Reason = "進房失敗"
			};

			response.Payload = responseContent.ToByteString();

			await messageQueueService.PublishAsync(
				"connect.send",
				response.ToByteArray()).ConfigureAwait(false);
		}
	}
}
