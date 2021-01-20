using System;
using System.Collections.Generic;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class ListPlayerQueryService : IQueryService<ListPlayerQuery, string>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public ListPlayerQueryService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async IAsyncEnumerable<string> QueryAsync(ListPlayerQuery query)
		{
			var roomQuery = new RoomSessionsRequest
			{
				Room = query.Room
			};

			var result = await m_MessageQueueService.RequestAsync("room.query", roomQuery.ToByteArray());
			var queryResponse = RoomSessionsResponse.Parser.ParseFrom(result.Data);

			foreach (var id in queryResponse.SessionIds)
				yield return id;
		}
	}
}
