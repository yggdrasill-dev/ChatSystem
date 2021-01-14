using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SessionServer.Models
{
	public class SessionRepository
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
	}
}
