using System;
using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class ListPlayerQueryService : IQueryService<ListPlayerQuery, PlayerInfo>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public ListPlayerQueryService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async IAsyncEnumerable<PlayerInfo> QueryAsync(ListPlayerQuery query)
		{
			var roomQuery = new RoomSessionsRequest
			{
				Room = query.Room
			};

			var result = await m_MessageQueueService.RequestAsync("room.query", roomQuery.ToByteArray());
			var queryResponse = RoomSessionsResponse.Parser.ParseFrom(result.Data);

			foreach (var player in queryResponse.Players)
				yield return new PlayerInfo(player.SessionId, player.ConnectorId, player.Name);
		}
	}
}
