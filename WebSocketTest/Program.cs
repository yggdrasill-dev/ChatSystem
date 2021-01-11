using ChatConnector.Protos;
using Google.Protobuf;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketTest
{
	class Program
	{
		static async Task Main(string[] args)
		{
			var socket = new ClientWebSocket();
			await socket.ConnectAsync(new Uri("wss://localhost:5001/ws"), CancellationToken.None).ConfigureAwait(false);

			var text = "Hello";
			var message = new ChatMessage();
			message.Subject = "test";
			message.Payload = ByteString.CopyFromUtf8(text);

			await socket.SendAsync(message.ToByteArray(), WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);

			//await socket.CloseAsync(WebSocketCloseStatus.Empty, null, CancellationToken.None).ConfigureAwait(false);

			Console.ReadKey();
		}
	}
}
