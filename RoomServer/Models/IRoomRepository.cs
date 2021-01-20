using System.Collections.Generic;
using System.Threading.Tasks;

namespace RoomServer.Models
{
	public interface IRoomRepository
	{
		ValueTask<string?> GetRoomBySessionIdAsync(string sessionId);
		ValueTask JoinRoomAsync(string sessionId, string room);
		ValueTask LeaveRoomAsync(string sessionId, string room);
		IAsyncEnumerable<string> QueryRoomsAsync();
		IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room);
	}
}