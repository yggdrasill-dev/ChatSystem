using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace SessionServer.Models
{
	internal class SessionRepository : ISessionRepository
	{
		private readonly ConcurrentDictionary<string, Registration> m_Sessions = new ConcurrentDictionary<string, Registration>();

		public ValueTask RegisterSessionAsync(Registration reg)
		{
			m_Sessions.TryAdd(reg.SessionId, reg);

			return ValueTask.CompletedTask;
		}

		public ValueTask UnregisterSessionBySessionIdAsync(string sessionId)
		{
			m_Sessions.TryRemove(sessionId, out var _);

			return ValueTask.CompletedTask;
		}

		public ValueTask<Registration?> GetRegistrationAsync(string sessionId)
		{
			if (m_Sessions.TryGetValue(sessionId, out var result))
				return new ValueTask<Registration?>(result);
			else
				return new ValueTask<Registration?>(default(Registration));
		}
	}
}
