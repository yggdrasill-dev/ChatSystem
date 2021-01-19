using System;
using System.Threading.Tasks;
using Common;

namespace SessionServer.Models
{
	public class RegisterSessionService : ICommandService<RegisterCommand>
	{
		private readonly ISessionRepository m_Repository;

		public RegisterSessionService(ISessionRepository repository)
		{
			m_Repository = repository ?? throw new ArgumentNullException(nameof(repository));
		}

		public ValueTask ExecuteAsync(RegisterCommand command)
		{
			return m_Repository.RegisterSessionAsync(
				new Registration(command.SessionId, command.ConnectorId, command.Name));
		}
	}
}
