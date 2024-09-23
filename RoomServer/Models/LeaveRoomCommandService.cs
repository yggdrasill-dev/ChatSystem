using System;
using System.Threading.Tasks;
using Common;
using Microsoft.Extensions.Logging;

namespace RoomServer.Models;

public class LeaveRoomCommandService(IRoomRepository roomRepository, ILogger<LeaveRoomCommandService> logger) : ICommandService<LeaveRoomCommand>
{
	public async ValueTask ExecuteAsync(LeaveRoomCommand command)
	{
		var room = string.IsNullOrEmpty(command.Room)
			? await roomRepository
				.GetRoomBySessionIdAsync(command.SessionId)
				.ConfigureAwait(false)
			: command.Room;

		logger.LogInformation("Leave room: ({SessionId}, {Room})", command.SessionId, room);

		if (!string.IsNullOrEmpty(room))
		{
			logger.LogInformation("leave room start");
			await roomRepository
				.LeaveRoomAsync(command.SessionId, room)
				.ConfigureAwait(false);
		}
	}
}
