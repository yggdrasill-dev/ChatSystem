using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class RegisterSessionCommandService : ICommandService<RegisterSessionCommand>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public RegisterSessionCommandService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async ValueTask ExecuteAsync(RegisterSessionCommand command)
		{
			var loginInfo = new RegisterRequest
			{
				SessionId = command.SessionId,
				ConnectorId = command.ConnectorId,
				Name = command.Name
			};

			await m_MessageQueueService.RequestAsync(
				"session.register",
				loginInfo.ToByteArray()).ConfigureAwait(false);
		}
	}
}
