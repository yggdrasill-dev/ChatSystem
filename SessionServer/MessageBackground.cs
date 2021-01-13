using Chat.Protos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using STAN.Client;
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SessionServer
{
	class MessageBackground : BackgroundService
	{
		private readonly IConnection m_Connection;
		private readonly ConcurrentDictionary<string, string> m_Sessions = new ConcurrentDictionary<string, string>();
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(ConnectionFactory connectionFactory, ILogger<MessageBackground> logger)
		{
			m_Connection = connectionFactory.CreateConnection();
			m_Logger = logger;
		}
		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("session.register", "registration", (sender, args) =>
			{
				var registration = PlayerRegistration.Parser.ParseFrom(args.Message.Data);

				m_Sessions.AddOrUpdate(
					registration.SessionId,
					registration.Name,
					(added, existing) => added);

				m_Connection.Publish(args.Message.Reply, Encoding.UTF8.GetBytes("registered"));

				m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
			});

			return Task.CompletedTask;
		}
	}
}
