using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatServer.Models.Endpoints
{
	public class ChatSendHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<PlayerInfoQuery, PlayerInfo> m_QueryPlayerService;
		private readonly IQueryService<ListPlayerQuery, string> m_RoomListService;
		private readonly ILogger<ChatSendHandler> m_Logger;

		public ChatSendHandler(
			IMessageQueueService messageQueueService,
			IQueryService<PlayerInfoQuery, PlayerInfo> queryPlayerService,
			IQueryService<ListPlayerQuery, string> roomListService,
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

			var content = ChatMessage.Parser.ParseFrom(packet.Payload);
			m_Logger.LogInformation($"Scope: {content.Scope}, Target: {content.Target}, Message: {content.Message}");

			var fromPlayerInfo = await m_QueryPlayerService
				.QueryAsync(new PlayerInfoQuery
				{
					SessionIds = new[] { packet.SessionId }
				})
				.FirstOrDefaultAsync()
				.ConfigureAwait(false);

			if (fromPlayerInfo?.SessionId != packet.SessionId)
				return;

			var sendContent = new ChatMessage
			{
				Scope = content.Scope,
				From = fromPlayerInfo.Name,
				Message = content.Message
			};

			var roomSessionIds = m_RoomListService
				.QueryAsync(new ListPlayerQuery
				{
					Room = "test"
				})
				.ToEnumerable()
				.ToArray();

			var roomPlayers = m_QueryPlayerService.QueryAsync(new PlayerInfoQuery
			{
				SessionIds = roomSessionIds.ToArray()
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

						sendMsg.SessionIds.AddRange(roomSessionIds);

						sendMsg.Payload = contentByteString;

						await m_MessageQueueService.PublishAsync(
							$"connect.send.{g.Key}", sendMsg.ToByteArray()).ConfigureAwait(false);
					}
					break;

				case Scope.Person:

					var targetName = content.Target;

					var matchedPlayer = await roomPlayers
						.Where(player => player.SessionId != fromPlayerInfo.SessionId)
						.Where(player => player.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
						.FirstOrDefaultAsync()
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

							await m_MessageQueueService.PublishAsync(
								$"connect.send.{matchedPlayer.ConnectorId}",
								sendMsg.ToByteArray()).ConfigureAwait(false);
						}
						else
						{
							var sendMsg = new SendPacket
							{
								Subject = "chat.receive"
							};

							sendMsg.Payload = sendContent.ToByteString();

							sendMsg.SessionIds.Add(matchedPlayer.SessionId);

							await m_MessageQueueService.PublishAsync(
								$"connect.send.{matchedPlayer.ConnectorId}",
								sendMsg.ToByteArray()).ConfigureAwait(false);

							sendMsg.SessionIds.Clear();
							sendMsg.SessionIds.Add(fromPlayerInfo.SessionId);

							await m_MessageQueueService.PublishAsync(
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

						await m_MessageQueueService.PublishAsync(
							$"connect.send.{fromPlayerInfo.ConnectorId}",
							sendMsg.ToByteArray()).ConfigureAwait(false);
					}
					break;
			}
		}
	}
}
