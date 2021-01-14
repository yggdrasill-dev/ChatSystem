using System;
using System.Threading.Tasks;
using Chat.Protos;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class SendQueueCommandService : ICommandService<SendQueueCommand>
	{
		private readonly MessageQueueService m_MessageQueueService;

		public SendQueueCommandService(MessageQueueService messageQueueService)
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
