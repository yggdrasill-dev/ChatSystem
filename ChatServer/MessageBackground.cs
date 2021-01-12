using Chat.Protos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ChatServer
{
	public class MessageBackground : BackgroundService
	{
		private readonly IConnection m_Connection;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(ConnectionFactory connectionFactory, ILogger<MessageBackground> logger)
		{
			m_Connection = connectionFactory.CreateConnection();
			m_Logger = logger;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("chat.send", "chat", (sender, args) =>
			{
				var packet = QueuePacket.Parser.ParseFrom(args.Message.Data);

				m_Logger.LogInformation($"Receive packet from {packet.SessionId}");

				var msg = ChatContent.Parser.ParseFrom(packet.Payload);
				m_Logger.LogInformation($"Scope: {msg.Scope}, Target: {msg.Target}, Message: {msg.Message}");
			});

			return Task.CompletedTask;
		}
	}
}
