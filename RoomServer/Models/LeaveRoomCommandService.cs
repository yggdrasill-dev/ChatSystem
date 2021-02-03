using System;
using System.Threading.Tasks;
using Common;
using Microsoft.Extensions.Logging;

namespace RoomServer.Models
{
	public class LeaveRoomCommandService : ICommandService<LeaveRoomCommand>
	{
		private readonly IRoomRepository m_RoomRepository;
		private readonly ILogger<LeaveRoomCommandService> m_Logger;

		public LeaveRoomCommandService(IRoomRepository roomRepository, ILogger<LeaveRoomCommandService> logger)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask ExecuteAsync(LeaveRoomCommand command)
		{
			var room = string.IsNullOrEmpty(command.Room)
				? await m_RoomRepository
					.GetRoomBySessionIdAsync(command.SessionId)
					.ConfigureAwait(false)
				: command.Room;

			m_Logger.LogInformation($"Leave room: ({command.SessionId}, {room})");

			if (!string.IsNullOrEmpty(room))
			{
				m_Logger.LogInformation("leave room start");
				await m_RoomRepository
					.LeaveRoomAsync(command.SessionId, room)
					.ConfigureAwait(false);
			}
		}
	}
}
