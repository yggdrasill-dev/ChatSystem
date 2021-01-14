using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace ChatConnector.Models
{
	public class WebSocketRepository : ConcurrentDictionary<string, WebSocket>
	{
	}
}
