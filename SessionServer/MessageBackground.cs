using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using SessionServer.Models;

namespace SessionServer
{
	internal class MessageBackground : BackgroundService
	{
		private readonly IServiceProvider m_ServiceProvider;
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ILogger<MessageBackground> m_Logger;

		public MessageBackground(
			IServiceProvider serviceProvider,
			IMessageQueueService messageQueueService,
			ILogger<MessageBackground> logger)
		{
			m_ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_Logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("session.register", "registration", async (sender, args) =>
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

				await m_MessageQueueService.PublishAsync(args.Message.Reply, Encoding.UTF8.GetBytes("registered"));

				m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
			}));

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("session.unregister", "unregister", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var registration = PlayerRegistration.Parser.ParseFrom(args.Message.Data);
				var command = scope.ServiceProvider.GetRequiredService<ICommandService<UnregisterSessionCommand>>();

				await command.ExecuteAsync(new UnregisterSessionCommand
				{
					SessionId = registration.SessionId
				});

				await m_MessageQueueService.PublishAsync(args.Message.Reply, Encoding.UTF8.GetBytes("registered"));

				m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
			}));

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("session.get", "get", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var query = PlayerQuery.Parser.ParseFrom(args.Message.Data);
				var service = scope.ServiceProvider.GetRequiredService<IGetService<GetPlayerBySessionIdQuery, Registration>>();

				var playerReg = await service.GetAsync(new GetPlayerBySessionIdQuery
				{
					SessionId = query.SessionId
				});

				var reply = new PlayerRegistration();

				if (playerReg != null)
				{
					reply.SessionId = playerReg.SessionId;
					reply.ConnectorId = playerReg.ConnectorId;
					reply.Name = playerReg.Name;
				}

				await m_MessageQueueService.PublishAsync(args.Message.Reply, reply.ToByteArray());
			}));

			stoppingToken.Register(() =>
			{
				foreach (var sub in subscriptions)
					sub.Dispose();
			});
		}
	}
}
