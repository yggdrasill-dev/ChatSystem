using System.Threading.Tasks;
using Common;

namespace RoomServer.Models;

public class GetRoomBySessionIdQueryService(IRoomRepository roomRepository) : IGetService<GetRoomBySessionIdQuery, string?>
{
	public ValueTask<string?> GetAsync(GetRoomBySessionIdQuery query)
	{
		return roomRepository.GetRoomBySessionIdAsync(query.SessionId);
	}
}
