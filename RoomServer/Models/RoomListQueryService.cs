using System.Collections.Generic;
using Common;

namespace RoomServer.Models;

public class RoomListQueryService(IRoomRepository roomRepository) : IQueryService<RoomListQuery, RoomInfo>
{
	public IAsyncEnumerable<RoomInfo> QueryAsync(RoomListQuery query)
	{
		return roomRepository.QueryRoomsAsync();
	}
}
