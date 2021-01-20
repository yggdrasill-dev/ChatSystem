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

		private readonly ConcurrentDictionary<string, string> m_Sessions =
			new ConcurrentDictionary<string, string>();

		public ValueTask JoinRoomAsync(string sessionId, string room)
		{
			var roomList = m_Rooms.GetOrAdd(room, roomKey => new HashSet<string>(StringComparer.InvariantCulture));

			lock (roomList)
				roomList.Add(sessionId);

			m_Sessions.AddOrUpdate(sessionId, room, (newRoom, oldRoom) =>
			{
				LeaveRoomAsync(sessionId, oldRoom).GetAwaiter().GetResult();

				return newRoom;
			});

			return ValueTask.CompletedTask;
		}

		public ValueTask LeaveRoomAsync(string sessionId, string room)
		{
			var roomList = m_Rooms.GetOrAdd(room, roomKey => new HashSet<string>(StringComparer.InvariantCulture));

			lock (roomList)
				roomList.Remove(sessionId);

			if (m_Sessions.TryGetValue(sessionId, out var inRoom) && inRoom == room)
				m_Sessions.TryRemove(sessionId, out _);

			return ValueTask.CompletedTask;
		}

		public IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room)
		{
			if (m_Rooms.TryGetValue(room, out var list))
			{
				lock (list)
					return list.ToAsyncEnumerable();
			}

			return AsyncEnumerable.Empty<string>();
		}

		public ValueTask<string?> GetRoomBySessionIdAsync(string sessionId)
		{
			if (m_Sessions.TryGetValue(sessionId, out var room))
				return ValueTask.FromResult<string?>(room);
			else
				return ValueTask.FromResult<string?>(null);
		}

		public IAsyncEnumerable<string> QueryRoomsAsync()
		{
			return m_Rooms.Keys.ToAsyncEnumerable();
		}
	}
}
