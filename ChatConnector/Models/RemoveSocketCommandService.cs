using System.Threading.Tasks;
using Common;

namespace ChatConnector.Models;

public class RemoveSocketCommandService(WebSocketRepository webSocketRepository) : ICommandService<RemoveSocketCommand>
{
	public ValueTask ExecuteAsync(RemoveSocketCommand command)
	{
		webSocketRepository.TryRemove(command.SessionId, out var _);

		return ValueTask.CompletedTask;
	}
}
