using System.Threading.Tasks;
using Common;

namespace RoomServer.Models;

public class JoinRoomCommandService(IRoomRepository roomRepository) : ICommandService<JoinRoomCommand>
{
	public ValueTask ExecuteAsync(JoinRoomCommand command)
		=> roomRepository.JoinRoomAsync(command.SessionId, command.Room, command.Password);
}
