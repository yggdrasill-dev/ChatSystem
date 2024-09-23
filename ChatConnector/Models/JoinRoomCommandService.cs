using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models;

public class JoinRoomCommandService(IMessageQueueService messageQueueService) : ICommandService<JoinRoomCommand>
{
	public async ValueTask ExecuteAsync(JoinRoomCommand command)
	{
		var request = new JoinRoomRequest
		{
			SessionId = command.SessionId,
			Room = command.Room
		};

		await messageQueueService.RequestAsync(
			"room.join",
			request.ToByteArray()).ConfigureAwait(false);
	}
}
