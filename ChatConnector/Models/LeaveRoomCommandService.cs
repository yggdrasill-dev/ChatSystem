using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models;

public class LeaveRoomCommandService(IMessageQueueService messageQueueService) : ICommandService<LeaveRoomCommand>
{
	public async ValueTask ExecuteAsync(LeaveRoomCommand command)
	{
		var request = new LeaveRoomRequest
		{
			SessionId = command.SessionId,
			Room = command.Room ?? string.Empty
		};

		await messageQueueService
			.RequestAsync("room.leave", request.ToByteArray())
			.ConfigureAwait(false);
	}
}
