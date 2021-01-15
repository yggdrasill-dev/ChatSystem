using System;
using System.Threading.Tasks;
using Common;

namespace SessionServer.Models
{
	public class RegisterSessionService : ICommandService<RegisterCommand>
	{
		private readonly SessionRepository m_Repository;

		public RegisterSessionService(SessionRepository repository)
		{
			m_Repository = repository ?? throw new ArgumentNullException(nameof(repository));
		}

		public ValueTask ExecuteAsync(RegisterCommand command)
		{
			return m_Repository.RegisterSessionAsync(command);
		}
	}
}
