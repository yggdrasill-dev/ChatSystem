using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

		public IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room)
		{
			if (m_Rooms.TryGetValue(room, out var list))
				return list.ToArray().ToAsyncEnumerable();

			return Array.Empty<string>().ToAsyncEnumerable();
		}
	}
}
