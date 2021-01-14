using System;
using System.Threading.Tasks;

namespace ChatConnector.Models
{
	public class AddSocketCommandService : ICommandService<AddSocketCommand>
	{
		private readonly WebSocketRepository m_WebSocketRepository;

		public AddSocketCommandService(WebSocketRepository webSocketRepository)
		{
			m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));
		}

		public ValueTask ExecuteAsync(AddSocketCommand command)
		{
			m_WebSocketRepository.TryAdd(command.SessionId, command.Socket);

			return ValueTask.CompletedTask;
		}
	}
}
