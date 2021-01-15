using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NATS.Client;

namespace ChatServer.Models
{
	public class MessageQueueService : BackgroundService
	{
		private readonly IConnection m_Connection;

		public MessageQueueService(ConnectionFactory connectionFactory)
		{
			m_Connection = connectionFactory.CreateConnection();
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
			

			return Task.CompletedTask;
		}
	}
}
