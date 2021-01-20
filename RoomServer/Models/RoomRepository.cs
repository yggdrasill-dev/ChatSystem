using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RoomServer.Models
{
	internal class RoomRepository : IRoomRepository
	{
		private readonly IDatabase m_Database;
		private readonly IServer m_Server;

		public RoomRepository(RedisConnectionFactory redisConnectionFactory)
		{
			if (redisConnectionFactory is null)
			{
				throw new ArgumentNullException(nameof(redisConnectionFactory));
			}

			m_Database = redisConnectionFactory.Database;
			m_Server = redisConnectionFactory.Server;
		}

		public async ValueTask JoinRoomAsync(string sessionId, string room)
		{
			var sessionInRoom = await m_Database
				.StringGetAsync($"Rooms:Sessions:{sessionId}")
				.ConfigureAwait(false);

			await m_Database
				.SetAddAsync($"Rooms:Room:{room}", sessionId)
				.ConfigureAwait(false);

			await m_Database
				.StringSetAsync($"Rooms:Sessions:{sessionId}", room)
				.ConfigureAwait(false);

			if (sessionInRoom.HasValue && (string)sessionInRoom != room)
				await LeaveRoomAsync(sessionId, sessionInRoom).ConfigureAwait(false);
		}

		public async ValueTask LeaveRoomAsync(string sessionId, string room)
		{
			var sessionInRoom = await m_Database
				.StringGetAsync($"Rooms:Sessions:{sessionId}")
				.ConfigureAwait(false);

			await m_Database.SetRemoveAsync($"Rooms:Room:{room}", sessionId).ConfigureAwait(false);

			if (sessionInRoom.HasValue && sessionInRoom == room)
				await m_Database
					.KeyDeleteAsync($"Rooms:Sessions:{sessionId}")
					.ConfigureAwait(false);
		}

		public async IAsyncEnumerable<string> QuerySessionsByRoomAsync(string room)
		{
			var sessionIds = await m_Database
				.SetMembersAsync($"Rooms:Room:{room}")
				.ConfigureAwait(false);

			foreach (var sessionId in sessionIds)
				yield return sessionId;
		}

		public async ValueTask<string?> GetRoomBySessionIdAsync(string sessionId)
		{
			var room = await m_Database
				.StringGetAsync($"Rooms:Sessions:{sessionId}")
				.ConfigureAwait(false);

			if (room.HasValue)
				return room;
			else
				return null;
		}

		public IAsyncEnumerable<string> QueryRoomsAsync()
		{
			return m_Server.KeysAsync(pattern: "Rooms:Room:*")
				.Select(key => (string)key)
				.Select(key => key.Split(":").Last());
		}
	}
}
