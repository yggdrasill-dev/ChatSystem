namespace ChatConnector.Models;

public struct JoinRoomCommand
{
	public string SessionId { get; set; }

	public string Room { get; set; }
}
