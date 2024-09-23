using System.Threading.Tasks;
using Common;

namespace SessionServer.Models;

public class RegisterSessionService(ISessionRepository repository) : ICommandService<RegisterCommand>
{
	public ValueTask ExecuteAsync(RegisterCommand command)
	{
		return repository.RegisterSessionAsync(
			new Registration(command.SessionId, command.ConnectorId, command.Name));
	}
}
