namespace ChatConnector.Models
{
	public struct LeaveRoomCommand
	{
		public string SessionId { get; set; }

		public string Room { get; set; }
	}
}
