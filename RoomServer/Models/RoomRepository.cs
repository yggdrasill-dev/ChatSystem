using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RoomServer.Models
{
	public class RoomRepository
	{
		private readonly ConcurrentDictionary<string, ConcurrentBag<string>> m_Rooms =
			new ConcurrentDictionary<string, ConcurrentBag<string>>();

		public ValueTask JoinRoomAsync(string sessionId, string room)
		{
			var roomList = m_Rooms.GetOrAdd(room, roomKey => new ConcurrentBag<string>());

			roomList.Add(sessionId);

			return ValueTask.CompletedTask;
		}
	}
}
