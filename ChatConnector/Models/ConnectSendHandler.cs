using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace ChatConnector.Models
{
	public class ConnectSendHandler : IMessageHandler
	{
		private readonly WebSocketRepository m_WebSocketRepository;

		public ConnectSendHandler(WebSocketRepository webSocketRepository)
		{
			m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var content = SendPacket.Parser.ParseFrom(msg.Data);
			var message = new Packet
			{
				Subject = content.Subject,
				Payload = content.Payload
			};
			var data = message.ToByteArray();

			foreach (var sessionId in content.SessionIds)
			{
				if (m_WebSocketRepository.TryGetValue(sessionId, out var socket))
					await socket.SendAsync(
						data,
						WebSocketMessageType.Binary,
						true,
						cancellationToken).ConfigureAwait(false);
			}
		}
	}
}
