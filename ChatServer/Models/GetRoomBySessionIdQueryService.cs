using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models
{
	public class GetRoomBySessionIdQueryService : IGetService<GetRoomBySessionidQuery, string?>
	{
		private readonly IMessageQueueService m_MessageQueueService;

		public GetRoomBySessionIdQueryService(IMessageQueueService messageQueueService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}
		public async ValueTask<string?> GetAsync(GetRoomBySessionidQuery query)
		{
			var request = new GetRoomBySessionIdRequest
			{
				SessionId = query.SessionId
			};

			var response = await m_MessageQueueService.RequestAsync(
				"room.session.get",
				request.ToByteArray()).ConfigureAwait(false);

			var getRoomResponse = GetRoomBySessionIdResponse.Parser.ParseFrom(response.Data);

			return getRoomResponse.Room;
		}
	}
}
