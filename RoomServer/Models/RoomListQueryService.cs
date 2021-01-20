using System.Collections.Generic;
using Common;

namespace RoomServer.Models
{
	public class RoomListQueryService : IQueryService<RoomListQuery, string>
	{
		private readonly RoomRepository m_RoomRepository;

		public RoomListQueryService(RoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository;
		}

		public IAsyncEnumerable<string> QueryAsync(RoomListQuery query)
		{
			return m_RoomRepository.QueryRoomsAsync();
		}
	}
}
