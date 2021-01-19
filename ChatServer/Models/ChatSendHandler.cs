﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatServer.Models
{
	public class ChatSendHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<GetPlayerQuery, PlayerInfo> m_QueryPlayerService;
		private readonly IQueryService<RoomListQuery, string> m_RoomListService;
		private readonly ILogger<ChatSendHandler> m_Logger;

		public ChatSendHandler(
			IMessageQueueService messageQueueService,
			IQueryService<GetPlayerQuery, PlayerInfo> queryPlayerService,
			IQueryService<RoomListQuery, string> roomListService,
			ILogger<ChatSendHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_QueryPlayerService = queryPlayerService ?? throw new ArgumentNullException(nameof(queryPlayerService));
			m_RoomListService = roomListService ?? throw new ArgumentNullException(nameof(roomListService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			m_Logger.LogInformation($"Receive packet from {packet.SessionId}");

			var content = ChatContent.Parser.ParseFrom(packet.Payload);
			m_Logger.LogInformation($"Scope: {content.Scope}, Target: {content.Target}, Message: {content.Message}");

			var playerInfo = await m_QueryPlayerService.QueryAsync(new GetPlayerQuery
			{
				SessionIds = new[] { packet.SessionId }
			}).FirstOrDefaultAsync().ConfigureAwait(false);

			if (playerInfo?.SessionId != packet.SessionId)
				return;

			var sendContent = new ChatContent
			{
				Scope = content.Scope,
				From = playerInfo.Name,
				Message = content.Message
			};

			var responseSessionIds = await m_RoomListService.QueryAsync(new RoomListQuery
			{
				Room = "test"
			}).ToArrayAsync().ConfigureAwait(false);

			var roomPlayers = m_QueryPlayerService.QueryAsync(new GetPlayerQuery
			{
				SessionIds = responseSessionIds
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

						sendMsg.SessionIds.AddRange(responseSessionIds);

						sendMsg.Payload = contentByteString;

						await m_MessageQueueService.PublishAsync(
							$"connect.send.{g.Key}", sendMsg.ToByteArray()).ConfigureAwait(false);
					}
					break;

				case Scope.Person:

					var targetName = content.Target;

					var matchedPlayer = await roomPlayers
						.Where(player => player.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
						.FirstOrDefaultAsync()
						.ConfigureAwait(false);

					if (matchedPlayer != null)
					{
						var sendMsg = new SendPacket
						{
							Subject = "chat.receive"
						};

						sendMsg.SessionIds.Add(matchedPlayer.SessionId);
						sendMsg.SessionIds.Add(packet.SessionId);

						sendContent.Target = matchedPlayer.Name;
						sendMsg.Payload = sendContent.ToByteString();

						await m_MessageQueueService.PublishAsync(
							$"connect.send.{matchedPlayer.ConnectorId}", sendMsg.ToByteArray()).ConfigureAwait(false);
					}
					break;
			}
		}
	}
}
