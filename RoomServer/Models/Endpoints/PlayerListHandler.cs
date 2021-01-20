using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Endpoints
{
	public class PlayerListHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IGetService<GetRoomBySessionIdQuery, string?> m_GetRoomService;
		private readonly IQueryService<PlayerInfoQuery, PlayerInfo> m_PlayerQueryService;
		private readonly IQueryService<RoomSessionsQuery, string> m_RoomListService;
		private readonly ILogger<PlayerListHandler> m_Logger;

		public PlayerListHandler(
			IMessageQueueService messageQueueService,
			IGetService<GetRoomBySessionIdQuery, string?> getRoomService,
			IQueryService<PlayerInfoQuery, PlayerInfo> playerQueryService,
			IQueryService<RoomSessionsQuery, string> roomListService,
			ILogger<PlayerListHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_GetRoomService = getRoomService ?? throw new ArgumentNullException(nameof(getRoomService));
			m_PlayerQueryService = playerQueryService ?? throw new ArgumentNullException(nameof(playerQueryService));
			m_RoomListService = roomListService ?? throw new ArgumentNullException(nameof(roomListService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			m_Logger.LogInformation($"chat.player.list => Receive packet from {packet.SessionId}");

			var playerInRoom = await m_GetRoomService
				.GetAsync(new GetRoomBySessionIdQuery
				{
					SessionId = packet.SessionId
				}).ConfigureAwait(false);

			if (string.IsNullOrEmpty(playerInRoom))
				return;

			var responseSessionIds = m_RoomListService.QueryAsync(new RoomSessionsQuery
			{
				Room = playerInRoom
			});

			var playersContent = new PlayerList();

			var allPlayers = m_PlayerQueryService.QueryAsync(new PlayerInfoQuery
			{
				SessionIds = await responseSessionIds.ToArrayAsync().ConfigureAwait(false)
			});

			playersContent.Players.AddRange(allPlayers.Select(player => player.Name).ToEnumerable());

			var sendMsg = new SendPacket
			{
				Subject = "chat.player.list",
				Payload = playersContent.ToByteString()
			};

			sendMsg.SessionIds.Add(packet.SessionId);

			await m_MessageQueueService.PublishAsync("connect.send", sendMsg.ToByteArray());
		}
	}
}
