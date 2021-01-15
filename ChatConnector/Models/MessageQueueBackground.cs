using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;

namespace ChatConnector.Models
{
	public class MessageQueueBackground : BackgroundService
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly WebSocketRepository m_WebSocketRepository;

		public MessageQueueBackground(IMessageQueueService messageQueueService, WebSocketRepository webSocketRepository)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_WebSocketRepository = webSocketRepository ?? throw new ArgumentNullException(nameof(webSocketRepository));
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("connect.send", async (sender, args) =>
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
			}));

			stoppingToken.Register(() =>
			{
				foreach (var sub in subscriptions)
					sub.Dispose();
			});
		}
	}
}
