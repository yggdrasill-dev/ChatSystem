using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace ChatConnector.Models.Handlers;

public class ConnectSendHandler(
	WebSocketRepository webSocketRepository,
	ILogger<ConnectSendHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var content = SendPacket.Parser.ParseFrom(msg.Data);
		var message = new Packet
		{
			Subject = content.Subject,
			Payload = content.Payload
		};
		var data = message.ToByteArray();

		logger.LogInformation("Send msg to {Subject}", content.Subject);

		foreach (var sessionId in content.SessionIds)
		{
			if (!webSocketRepository.TryGetValue(sessionId, out var socket))
				continue;

			await socket.SendAsync(
				data,
				WebSocketMessageType.Binary,
				true,
				cancellationToken).ConfigureAwait(false);
		}
	}
}
