using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace RoomServer.Models.Handlers
{
	public class GetRoomBySessionIdHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IGetService<GetRoomBySessionIdQuery, string?> m_GetRoomService;

		public GetRoomBySessionIdHandler(
			IMessageQueueService messageQueueService,
			IGetService<GetRoomBySessionIdQuery, string?> getRoomService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_GetRoomService = getRoomService ?? throw new ArgumentNullException(nameof(getRoomService));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var request = GetRoomBySessionIdRequest.Parser.ParseFrom(msg.Data);

			var room = await m_GetRoomService
				.GetAsync(new GetRoomBySessionIdQuery
				{
					SessionId = request.SessionId
				})
				.ConfigureAwait(false);

			var response = new GetRoomBySessionIdResponse
			{
				Room = room
			};

			await m_MessageQueueService.PublishAsync(
				msg.Reply,
				response.ToByteArray()).ConfigureAwait(false);
		}
	}
}
