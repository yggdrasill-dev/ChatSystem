using System.Collections.Generic;
using Common;

namespace RoomServer.Models;

public class RoomSessionsQueryService(IRoomRepository roomRepository) : IQueryService<RoomSessionsQuery, string>
{
	public IAsyncEnumerable<string> QueryAsync(RoomSessionsQuery query)
	{
		return roomRepository.QuerySessionsByRoomAsync(query.Room);
	}
}
