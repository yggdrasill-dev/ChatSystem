using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoomServer.Models
{
	public interface IRoomRepository
	{
		ValueTask<string?> GetRoomBySessionIdAsync(string sessionId);
		ValueTask JoinRoomAsync(string sessionId, string room, string password);
		ValueTask LeaveRoomAsync(string sessionId, string room);
		IAsyncEnumerable<RoomInfo> QueryRoomsAsync();
		IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room);
	}
}