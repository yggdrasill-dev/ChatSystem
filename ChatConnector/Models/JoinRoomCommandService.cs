using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models
{
	public class JoinRoomCommandService : ICommandService<JoinRoomCommand>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public JoinRoomCommandService(IMessageQueueService messageQueueService)
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
