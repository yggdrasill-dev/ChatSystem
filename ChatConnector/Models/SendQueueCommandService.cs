using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class SendQueueCommandService : ICommandService<SendQueueCommand>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public SendQueueCommandService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public ValueTask ExecuteAsync(SendQueueCommand command)
		{
			var packet = new QueuePacket
			{
				SessionId = command.SessionId,
				Payload = command.Payload
			};

			return m_MessageQueueService.PublishAsync(command.Subject, packet.ToByteArray());
		}
	}
}
