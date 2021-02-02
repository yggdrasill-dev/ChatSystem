using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatConnector.Models.Handlers
{
	public class ConnectSendHandler : IMessageHandler
	{
		private readonly WebSocketRepository m_WebSocketRepository;
		private readonly ILogger<ConnectSendHandler> m_Logger;

		public ConnectSendHandler(
			WebSocketRepository webSocketRepository,
			ILogger<ConnectSendHandler> logger)
		{
			m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

			m_Logger.LogInformation($"Send msg to {content.Subject}");

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
