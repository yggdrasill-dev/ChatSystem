using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace RoomServer.Models.Handlers
{
	public class QuerySessionsByRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<RoomSessionsQuery, string> m_RoomSessionsService;
		private readonly IQueryService<PlayerInfoQuery, PlayerInfo> m_PlayerInfoService;

		public QuerySessionsByRoomHandler(
			IMessageQueueService messageQueueService,
			IQueryService<RoomSessionsQuery, string> roomSessionsService,
			IQueryService<PlayerInfoQuery, PlayerInfo> playerInfoService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_RoomSessionsService = roomSessionsService ?? throw new ArgumentNullException(nameof(roomSessionsService));
			m_PlayerInfoService = playerInfoService ?? throw new ArgumentNullException(nameof(playerInfoService));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var request = RoomSessionsRequest.Parser.ParseFrom(msg.Data);

			var sessionIds = m_RoomSessionsService
				.QueryAsync(new RoomSessionsQuery
				{
					Room = request.Room
				})
				.ToEnumerable()
				.ToArray();

			var allPlayers = m_PlayerInfoService.QueryAsync(new PlayerInfoQuery
			{
				SessionIds = sessionIds
			});

			var response = new RoomSessionsResponse();

			response.Players.AddRange(allPlayers
				.Select(player => new Chat.Protos.PlayerInfo
				{
					SessionId = player.SessionId,
					ConnectorId = player.ConnectorId,
					Name = player.Name
				})
				.ToEnumerable());

			await m_MessageQueueService.PublishAsync(msg.Reply, response.ToByteArray());
		}
	}
}
