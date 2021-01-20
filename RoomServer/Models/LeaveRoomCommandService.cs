using System;
using System.Threading.Tasks;
using Common;

namespace RoomServer.Models
{
	public class LeaveRoomCommandService : ICommandService<LeaveRoomCommand>
	{
		private readonly IRoomRepository m_RoomRepository;

		public LeaveRoomCommandService(IRoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
		}

		public ValueTask ExecuteAsync(LeaveRoomCommand command)
		{
			return m_RoomRepository.LeaveRoomAsync(command.SessionId, command.Room);
		}
	}
}
