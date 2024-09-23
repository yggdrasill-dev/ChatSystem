using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace SessionServer.Models;

internal class SessionRepository(IDatabase database) : ISessionRepository
{
	public async ValueTask RegisterSessionAsync(Registration reg)
	{
		await database.StringSetAsync(
			$"Sessions:{reg.SessionId}",
			JsonSerializer.Serialize(reg),
			expiry: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
	}

	public async ValueTask UnregisterSessionBySessionIdAsync(string sessionId)
	{
		await database.KeyDeleteAsync($"Sessions:{sessionId}").ConfigureAwait(false);
	}

	public async ValueTask<Registration?> GetRegistrationAsync(string sessionId)
	{
		var result = await database
			.StringGetAsync($"Sessions:{sessionId}")
			.ConfigureAwait(false);

		return result.IsNullOrEmpty ? null : JsonSerializer.Deserialize<Registration>(result!);
	}

	public async ValueTask ActiveSessionAsync(string sessionId)
	{
		await database.KeyExpireAsync(
			$"Sessions:{sessionId}",
			TimeSpan.FromSeconds(30)).ConfigureAwait(false);
	}
}
