using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatServer.Models;

public class GetRoomBySessionIdQueryService(IMessageQueueService messageQueueService) : IGetService<GetRoomBySessionidQuery, string?>
{
	public async ValueTask<string?> GetAsync(GetRoomBySessionidQuery query)
	{
		var request = new GetRoomBySessionIdRequest
		{
			SessionId = query.SessionId
		};

		var response = await messageQueueService.RequestAsync(
			"room.session.get",
			request.ToByteArray()).ConfigureAwait(false);

		var getRoomResponse = GetRoomBySessionIdResponse.Parser.ParseFrom(response.Data);

		return getRoomResponse.Room;
	}
}
