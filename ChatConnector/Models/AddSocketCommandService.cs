using System;
using System.Threading.Tasks;
using Common;

namespace ChatConnector.Models;

public class AddSocketCommandService(WebSocketRepository webSocketRepository) : ICommandService<AddSocketCommand>
{
	private readonly WebSocketRepository m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));

	public ValueTask ExecuteAsync(AddSocketCommand command)
	{
		m_WebSocketRepository.TryAdd(command.SessionId, command.Socket);

		return ValueTask.CompletedTask;
	}
}
