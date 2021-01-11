using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SessionServer.Protos;
using STAN.Client;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SessionServer
{
	class MessageBackground : BackgroundService
	{
		private readonly IStanConnection m_StanConnection;
		private readonly ConcurrentDictionary<string, string> m_Sessions = new ConcurrentDictionary<string, string>();
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(StanConnectionFactory connectionFactory, ILogger<MessageBackground> logger)
		{
			var opts = StanOptions.GetDefaultOptions();
			m_StanConnection = connectionFactory.CreateConnection("test-cluster", Guid.NewGuid().ToString(), opts);
			m_Logger = logger;
		}
		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_StanConnection.Subscribe("session.register", "registration", (sender, args) =>
			{
				var registration = PlayerRegistration.Parser.ParseFrom(args.Message.Data);

				m_Sessions.AddOrUpdate(
					registration.SessionId,
					registration.Name,
					(added, existing) => added);

				m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
			});

			return Task.CompletedTask;
		}
	}
}
