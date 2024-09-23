using System;
using System.Threading.Tasks;
using Common;

namespace SessionServer.Models;

public class UnregisterSessionCommandService(ISessionRepository sessionRepository) : ICommandService<UnregisterSessionCommand>
{
	public ValueTask ExecuteAsync(UnregisterSessionCommand command)
	{
		return sessionRepository.UnregisterSessionBySessionIdAsync(command.SessionId);
	}
}
