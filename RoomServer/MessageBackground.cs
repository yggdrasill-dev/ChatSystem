using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client;
using RoomServer.Models;
using System.Linq;
using Google.Protobuf;
using Common;
using System.Collections.Generic;

namespace RoomServer
{
	public class MessageBackground : BackgroundService
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
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("room.join", "join", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var request = JoinRoomRequest.Parser.ParseFrom(args.Message.Data);
				var command = scope.ServiceProvider.GetRequiredService<ICommandService<JoinRoomCommand>>();

				await command.ExecuteAsync(new JoinRoomCommand
				{
					SessionId = request.SessionId,
					Room = request.Room
				});

				await m_MessageQueueService.PublishAsync(args.Message.Reply, Encoding.UTF8.GetBytes("joined"));

				m_Logger.LogInformation($"({request.SessionId}, {request.Room}) joined.");
			}));

			subscriptions.Add(await m_MessageQueueService.SubscribeAsync("room.query", "query", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var request = RoomSessionsRequest.Parser.ParseFrom(args.Message.Data);
				var queryService = scope.ServiceProvider.GetRequiredService<IQueryService<RoomSessionsQuery, string>>();

				var sessionIds = await queryService.QueryAsync(new RoomSessionsQuery
				{
					Room = request.Room
				}).ToArrayAsync();

				var response = new RoomSessionsResponse();

				response.SessionIds.AddRange(sessionIds);

				await m_MessageQueueService.PublishAsync(args.Message.Reply, response.ToByteArray());
			}));

			stoppingToken.Register(() =>
			{
				foreach (var sub in subscriptions)
					sub.Dispose();
			});
		}
	}
}
