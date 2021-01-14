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

namespace RoomServer
{
	public class MessageBackground : BackgroundService
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
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		protected override Task ExecuteAsync(CancellationToken stoppingToken)
		{
			m_Connection.SubscribeAsync("room.join", "join", async (sender, args) =>
			{
				using var scope = m_ServiceProvider.CreateScope();

				var request = JoinRoomRequest.Parser.ParseFrom(args.Message.Data);
				var command = scope.ServiceProvider.GetRequiredService<ICommandService<JoinRoomCommand>>();

				await command.ExecuteAsync(new JoinRoomCommand
				{
					SessionId = request.SessionId,
					Room = request.Room
				});

				m_Connection.Publish(args.Message.Reply, Encoding.UTF8.GetBytes("joined"));

				m_Logger.LogInformation($"({request.SessionId}, {request.Room}) joined.");
			});

			return Task.CompletedTask;
		}
	}
}
