using System.Collections.Generic;
using Common;

namespace RoomServer.Models
{
	public class RoomListQueryService : IQueryService<RoomListQuery, RoomInfo>
	{
		private readonly IRoomRepository m_RoomRepository;

		public RoomListQueryService(IRoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository;
		}

		public IAsyncEnumerable<RoomInfo> QueryAsync(RoomListQuery query)
		{
			return m_RoomRepository.QueryRoomsAsync();
		}
	}
}
