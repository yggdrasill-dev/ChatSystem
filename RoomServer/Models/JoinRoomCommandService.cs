using System;
using System.Threading.Tasks;

namespace RoomServer.Models
{
	public class JoinRoomCommandService : ICommandService<JoinRoomCommand>
	{
		private readonly RoomRepository m_RoomRepository;

		public JoinRoomCommandService(RoomRepository roomRepository)
		{
			m_RoomRepository = roomRepository ?? throw new ArgumentNullException(nameof(roomRepository));
		}

		public ValueTask ExecuteAsync(JoinRoomCommand command)
		{
			return m_RoomRepository.JoinRoomAsync(command.SessionId, command.Room);
		}
	}
}
