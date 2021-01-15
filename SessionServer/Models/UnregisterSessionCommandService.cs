using System;
using System.Threading.Tasks;
using Common;

namespace SessionServer.Models
{
	public class UnregisterSessionCommandService : ICommandService<UnregisterSessionCommand>
	{
		private readonly SessionRepository m_SessionRepository;

		public UnregisterSessionCommandService(SessionRepository sessionRepository)
		{
			m_SessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
		}

		public ValueTask ExecuteAsync(UnregisterSessionCommand command)
		{
			return m_SessionRepository.UnregisterSessionBySessionIdAsync(command.SessionId);
		}
	}
}
