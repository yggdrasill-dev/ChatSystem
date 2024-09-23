using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace RoomServer.Models.Handlers;

public class GetRoomBySessionIdHandler(
	IMessageQueueService messageQueueService,
	IGetService<GetRoomBySessionIdQuery, string?> getRoomService) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var request = GetRoomBySessionIdRequest.Parser.ParseFrom(msg.Data);

		var room = await getRoomService
			.GetAsync(new GetRoomBySessionIdQuery
			{
				SessionId = request.SessionId
			})
			.ConfigureAwait(false);

		var response = new GetRoomBySessionIdResponse
		{
			Room = room ?? string.Empty
		};

		await messageQueueService.PublishAsync(
			msg.Reply,
			response.ToByteArray()).ConfigureAwait(false);
	}
}
