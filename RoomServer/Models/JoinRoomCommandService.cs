using System;
using System.Threading.Tasks;
using Common;

namespace RoomServer.Models
{
	public class JoinRoomCommandService : ICommandService<JoinRoomCommand>
	{
		private readonly IRoomRepository m_RoomRepository;

		public JoinRoomCommandService(IRoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
		}

		public ValueTask ExecuteAsync(JoinRoomCommand command)
		{
			return m_RoomRepository.JoinRoomAsync(command.SessionId, command.Room);
		}
	}
}
