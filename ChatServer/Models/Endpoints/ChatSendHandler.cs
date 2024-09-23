﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatServer.Models.Endpoints;

public class ChatSendHandler(
	IMessageQueueService messageQueueService,
	IGetService<PlayerInfoQuery, PlayerInfo?> queryPlayerService,
	IQueryService<ListPlayerQuery, PlayerInfo> roomListService,
	IGetService<GetRoomBySessionidQuery, string?> getRoomService,
	ILogger<ChatSendHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var packet = QueuePacket.Parser.ParseFrom(msg.Data);

		logger.LogInformation("Receive packet from {SessionId}", packet.SessionId);

		var content = ChatMessage.Parser.ParseFrom(packet.Payload);
		logger.LogInformation(
			"Scope: {Scope}, Target: {Target}, Message: {Message}",
			content.Scope,
			content.Target,
			content.Message);

		var fromPlayerInfo = await queryPlayerService
			.GetAsync(new PlayerInfoQuery
			{
				SessionId = packet.SessionId
			})
			.ConfigureAwait(false);

		if (fromPlayerInfo?.SessionId != packet.SessionId)
			return;

		var room = await getRoomService.GetAsync(new GetRoomBySessionidQuery
		{
			SessionId = fromPlayerInfo.SessionId
		}).ConfigureAwait(false);

		if (string.IsNullOrEmpty(room))
			return;

		var sendContent = new ChatMessage
		{
			Scope = content.Scope,
			From = fromPlayerInfo.Name,
			Message = content.Message
		};

		var roomPlayers = roomListService
			.QueryAsync(new ListPlayerQuery
			{
				Room = room
			});

		switch (content.Scope)
		{
			case Scope.Room:
				var contentByteString = sendContent.ToByteString();

				await foreach (var g in roomPlayers.GroupBy(player => player.ConnectorId))
				{
					var sendMsg = new SendPacket
					{
						Subject = "chat.receive"
					};

					sendMsg.SessionIds.AddRange(g.Select(player => player.SessionId).ToEnumerable());

					sendMsg.Payload = contentByteString;

					await messageQueueService.PublishAsync(
						$"connect.send.{g.Key}", sendMsg.ToByteArray()).ConfigureAwait(false);
				}

				break;

			case Scope.Person:

				var targetName = content.Target;

				var matchedPlayer = await roomPlayers
					.Where(player => player.SessionId != fromPlayerInfo.SessionId)
					.Where(player => player.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
					.FirstOrDefaultAsync(cancellationToken: cancellationToken)
					.ConfigureAwait(false);

				if (matchedPlayer != null)
				{
					sendContent.Target = matchedPlayer.Name;

					if (fromPlayerInfo.ConnectorId == matchedPlayer.ConnectorId)
					{
						var sendMsg = new SendPacket
						{
							Subject = "chat.receive"
						};

						sendMsg.SessionIds.Add(matchedPlayer.SessionId);
						sendMsg.SessionIds.Add(fromPlayerInfo.SessionId);

						sendMsg.Payload = sendContent.ToByteString();

						await messageQueueService.PublishAsync(
							$"connect.send.{matchedPlayer.ConnectorId}",
							sendMsg.ToByteArray()).ConfigureAwait(false);
					}
					else
					{
						var sendMsg = new SendPacket
						{
							Subject = "chat.receive",
							Payload = sendContent.ToByteString()
						};

						sendMsg.SessionIds.Add(matchedPlayer.SessionId);

						await messageQueueService.PublishAsync(
							$"connect.send.{matchedPlayer.ConnectorId}",
							sendMsg.ToByteArray()).ConfigureAwait(false);

						sendMsg.SessionIds.Clear();
						sendMsg.SessionIds.Add(fromPlayerInfo.SessionId);

						await messageQueueService.PublishAsync(
							$"connect.send.{fromPlayerInfo.ConnectorId}",
							sendMsg.ToByteArray()).ConfigureAwait(false);
					}
				}
				else
				{
					var sendMsg = new SendPacket
					{
						Subject = "chat.receive"
					};

					sendMsg.SessionIds.Add(fromPlayerInfo.SessionId);

					sendContent.Target = fromPlayerInfo.Name;

					var notFoundPlayerContent = new ChatMessage
					{
						Scope = Scope.System,
						Message = $"Can't send message to {targetName}",
						From = "System",
						Target = fromPlayerInfo.Name
					};

					sendMsg.Payload = notFoundPlayerContent.ToByteString();

					await messageQueueService.PublishAsync(
						$"connect.send.{fromPlayerInfo.ConnectorId}",
						sendMsg.ToByteArray()).ConfigureAwait(false);
				}

				break;
		}
	}
}
