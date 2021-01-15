using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RoomServer.Models
{
	public class RoomRepository
	{
		private readonly ConcurrentDictionary<string, HashSet<string>> m_Rooms =
			new ConcurrentDictionary<string, HashSet<string>>();

		public ValueTask JoinRoomAsync(string sessionId, string room)
		{
			var roomList = m_Rooms.GetOrAdd(room, roomKey => new HashSet<string>(StringComparer.InvariantCulture));

			lock (roomList)
				roomList.Add(sessionId);

			return ValueTask.CompletedTask;
		}

		public ValueTask LeaveRoomAsync(string sessionId, string room)
		{
			var roomList = m_Rooms.GetOrAdd(room, roomKey => new HashSet<string>(StringComparer.InvariantCulture));

			lock (roomList)
				roomList.Remove(sessionId);

			return ValueTask.CompletedTask;
		}

		public IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room)
		{
			if (m_Rooms.TryGetValue(room, out var list))
			{
				lock (list)
					return list.ToArray().ToAsyncEnumerable();
			}

			return Array.Empty<string>().ToAsyncEnumerable();
		}
	}
}
