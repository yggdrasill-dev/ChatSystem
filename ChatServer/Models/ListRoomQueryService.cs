using System;
using System.Collections.Generic;
using Chat.Protos;
using Common;

namespace ChatServer.Models
{
	public class ListRoomQueryService : IQueryService<ListRoomQuery, string>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public ListRoomQueryService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async IAsyncEnumerable<string> QueryAsync(ListRoomQuery query)
		{
			var result = await m_MessageQueueService
				.RequestAsync("room.list", null)
				.ConfigureAwait(false);

			var response = RoomsResponse.Parser.ParseFrom(result.Data);

			foreach (var room in response.Rooms)
				yield return room;
		}
	}
}
