using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class UnregisterSessionCommandService : ICommandService<UnregisterSessionCommand>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public UnregisterSessionCommandService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async ValueTask ExecuteAsync(UnregisterSessionCommand command)
		{
			var loginInfo = new UnregisterRequest
			{
				SessionId = command.SessionId
			};

			await m_MessageQueueService.RequestAsync(
				"session.unregister",
				loginInfo.ToByteArray()).ConfigureAwait(false);
		}
	}
}
