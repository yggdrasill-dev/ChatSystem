using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using SessionServer.Models;

namespace SessionServer
{
	internal class MessageBackground : BackgroundService
	{
		private readonly IConnection m_Connection;
		private readonly IServiceProvider m_ServiceProvider;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(
			ConnectionFactory connectionFactory,
			IServiceProvider serviceProvider,
			ILogger<MessageBackground> logger)
		{
			m_Connection = connectionFactory.CreateConnection();
			m_ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			m_Logger = logger;
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("session.register", "registration", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var registration = PlayerRegistration.Parser.ParseFrom(args.Message.Data);
				var command = scope.ServiceProvider.GetRequiredService<ICommandService<RegisterCommand>>();

				await command.ExecuteAsync(new RegisterCommand
				{
					SessionId = registration.SessionId,
					ConnectorId = registration.ConnectorId,
					Name = registration.Name
				});

				m_Connection.Publish(args.Message.Reply, Encoding.UTF8.GetBytes("registered"));

				m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
			});

			return Task.CompletedTask;
		}
	}
}
