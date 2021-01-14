using System.Net.WebSockets;

namespace ChatConnector.Models
{
	public struct AddSocketCommand
	{
		public string SessionId { get; set; }

		public WebSocket Socket { get; set; }
	}
}
