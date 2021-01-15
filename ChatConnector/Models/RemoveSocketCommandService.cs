using System.Threading.Tasks;
using Common;

namespace ChatConnector.Models
{
	public class RemoveSocketCommandService : ICommandService<RemoveSocketCommand>
	{
		private readonly WebSocketRepository m_WebSocketRepository;

		public RemoveSocketCommandService(WebSocketRepository webSocketRepository)
		{
			m_WebSocketRepository = webSocketRepository;
		}

		public ValueTask ExecuteAsync(RemoveSocketCommand command)
		{
			m_WebSocketRepository.TryRemove(command.SessionId, out var _);

			return ValueTask.CompletedTask;
		}
	}
}
