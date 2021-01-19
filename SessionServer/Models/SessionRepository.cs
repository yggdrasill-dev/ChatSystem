using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace SessionServer.Models
{
	internal class SessionRepository : ISessionRepository
	{
		private readonly IDatabase m_Database;

		public SessionRepository(IDatabase database)
		{
			m_Database = database ?? throw new System.ArgumentNullException(nameof(database));
		}

		public async ValueTask RegisterSessionAsync(Registration reg)
		{
			await m_Database.StringSetAsync(
				$"Sessions:{reg.SessionId}",
				JsonSerializer.Serialize(reg),
				expiry: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}

		public async ValueTask UnregisterSessionBySessionIdAsync(string sessionId)
		{
			await m_Database.KeyDeleteAsync($"Sessions:{sessionId}").ConfigureAwait(false);
		}

		public async ValueTask<Registration?> GetRegistrationAsync(string sessionId)
		{
			var result = await m_Database
				.StringGetAsync($"Sessions:{sessionId}")
				.ConfigureAwait(false);

			if (result.IsNullOrEmpty)
				return null;
			else
				return JsonSerializer.Deserialize<Registration>(result);
		}

		public async ValueTask ActiveSessionAsync(string sessionId)
		{
			await m_Database.KeyExpireAsync(
				$"Sessions:{sessionId}",
				TimeSpan.FromSeconds(30)).ConfigureAwait(false);
		}
	}
}
