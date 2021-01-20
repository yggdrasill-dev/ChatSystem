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
	public class QueryRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<RoomSessionsQuery, string> m_RoomSessionsService;

		public QueryRoomHandler(
			IMessageQueueService messageQueueService,
			IQueryService<RoomSessionsQuery, string> roomSessionsService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_RoomSessionsService = roomSessionsService ?? throw new ArgumentNullException(nameof(roomSessionsService));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var request = RoomSessionsRequest.Parser.ParseFrom(msg.Data);

			var sessionIds = await m_RoomSessionsService.QueryAsync(new RoomSessionsQuery
			{
				Room = request.Room
			}).ToArrayAsync();

			var response = new RoomSessionsResponse();

			response.SessionIds.AddRange(sessionIds);

			await m_MessageQueueService.PublishAsync(msg.Reply, response.ToByteArray());
		}
	}
}
