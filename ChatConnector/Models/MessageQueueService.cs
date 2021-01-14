using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using NATS.Client;

namespace ChatConnector.Models
{
	public class MessageQueueService : BackgroundService
	{
		private readonly IConnection m_Connection;
		private readonly WebSocketRepository m_WebSocketRepository;

		public MessageQueueService(ConnectionFactory connectionFactory, WebSocketRepository webSocketRepository)
		{
			m_Connection = connectionFactory.CreateConnection();
			m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));
		}

		public ValueTask PublishAsync(string subject, byte[] data)
		{
			m_Connection.Publish(subject, data);

			return ValueTask.CompletedTask;
		}

		public async ValueTask<Msg> RequestAsync(string subject, byte[] data)
		{
			return await m_Connection.RequestAsync(subject, data).ConfigureAwait(false);
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("connect.send", async (sender, args) =>
			{
				var content = SendPacket.Parser.ParseFrom(args.Message.Data);
				var msg = new ChatMessage
				{
					Subject = "connect.receive",
					Payload = content.Payload
				};
				var data = msg.ToByteArray();

				foreach (var sessionId in content.SessionIds)
				{
					if (m_WebSocketRepository.TryGetValue(sessionId, out var socket))
						await socket.SendAsync(
							data,
							WebSocketMessageType.Binary,
							true,
							CancellationToken.None).ConfigureAwait(false);
				}
			});

			return Task.CompletedTask;
		}
	}
}
