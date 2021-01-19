using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common
{
	internal class MessageQueueBackground : BackgroundService
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IServiceProvider m_ServiceProvider;
		private readonly IEnumerable<ISubscribeRegistration> m_Subscribes;
		private readonly ILogger<MessageQueueBackground> m_Logger;

		public MessageQueueBackground(
			IMessageQueueService messageQueueService,
			IServiceProvider serviceProvider,
			IEnumerable<ISubscribeRegistration> subscribes,
			ILogger<MessageQueueBackground> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			m_Subscribes = subscribes ?? throw new ArgumentNullException(nameof(subscribes));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			var subscriptions = new List<IDisposable>();

			foreach (var registration in m_Subscribes)
				subscriptions.Add(
					await registration.SubscribeAsync(
						m_MessageQueueService,
						m_ServiceProvider,
						m_Logger,
						stoppingToken).ConfigureAwait(false));

			stoppingToken.Register(() =>
			{
				foreach (var sub in subscriptions)
					sub.Dispose();
			});
		}
	}
}
