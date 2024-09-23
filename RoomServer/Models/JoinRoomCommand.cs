namespace RoomServer.Models;

public struct JoinRoomCommand
{
	public string SessionId { get; set; }

	public string Room { get; set; }

	public string Password { get; set; }
}
