using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace RoomServer.Models
{
	internal class RoomRepository : IRoomRepository
	{
		private readonly IDatabase m_Database;
		private readonly IServer m_Server;
		private readonly ILogger<RoomRepository> m_Logger;

		public RoomRepository(RedisConnectionFactory redisConnectionFactory, ILogger<RoomRepository> logger)
		{
			if (redisConnectionFactory is null)
			{
				throw new ArgumentNullException(nameof(redisConnectionFactory));
			}

			m_Database = redisConnectionFactory.Database;
			m_Server = redisConnectionFactory.Server;
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask JoinRoomAsync(string sessionId, string room, string password)
		{
			var roomHasCreated = await m_Database
				.KeyExistsAsync($"Rooms:Room:{room}")
				.ConfigureAwait(false);

			var roomPasswordHash = await m_Database
				.StringGetAsync($"Rooms:Hash:{room}")
				.ConfigureAwait(false);

			if (roomHasCreated && roomPasswordHash.HasValue)
			{
				var verified = false;

				if (!string.IsNullOrEmpty(password))
				{
					verified = BCrypt.Net.BCrypt.Verify(password, roomPasswordHash);
				}

				if (!verified)
					throw new Exception("Room has protected");
			}

			if (!roomHasCreated)
			{
				if (!string.IsNullOrEmpty(password))
					await m_Database.StringSetAsync(
						$"Rooms:Hash:{room}",
						BCrypt.Net.BCrypt.HashPassword(password)).ConfigureAwait(false);
				else
					await m_Database.KeyDeleteAsync($"Rooms:Hash:{room}").ConfigureAwait(false);
			}

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

			m_Logger.LogInformation($"Session {sessionId} in {sessionInRoom}, leave room {room}");

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

		public IAsyncEnumerable<RoomInfo> QueryRoomsAsync()
		{
			var rooms = m_Server.KeysAsync(pattern: "Rooms:Room:*")
				.Select(key => (string)key)
				.Select(key => key.Split(":").Last());
			var hashes = m_Server.KeysAsync(pattern: "Rooms:Hash:*")
				.Select(key => (string)key)
				.Select(key => key.Split(":").Last());

			return from room in rooms
				   join hash in hashes
					   on room equals hash into g
				   from hash in g.DefaultIfEmpty()
				   select new RoomInfo(room, !string.IsNullOrEmpty(hash));
		}
	}
}