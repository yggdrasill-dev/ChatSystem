using System;
using System.Threading.Tasks;
using Chat.Protos;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class JoinRoomCommandService : ICommandService<JoinRoomCommand>
	{
		private readonly MessageQueueService m_MessageQueueService;

		public JoinRoomCommandService(MessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public async ValueTask ExecuteAsync(JoinRoomCommand command)
		{
			var request = new JoinRoomRequest
			{
				SessionId = command.SessionId,
				Room = command.Room
			};

			await m_MessageQueueService.RequestAsync(
				"room.join",
				request.ToByteArray()).ConfigureAwait(false);
		}
	}
}
