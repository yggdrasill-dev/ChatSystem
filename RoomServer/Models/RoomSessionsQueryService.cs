using System;
using System.Collections.Generic;
using Common;

namespace RoomServer.Models
{
	public class RoomSessionsQueryService : IQueryService<RoomSessionsQuery, string>
	{
		private readonly RoomRepository m_RoomRepository;

		public RoomSessionsQueryService(RoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
		}

		public IAsyncEnumerable<string> QueryAsync(RoomSessionsQuery query)
		{
			return m_RoomRepository.QuerySessionsByRoomAsync(query.Room);
		}
	}
}
