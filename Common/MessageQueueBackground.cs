using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common;

internal class MessageQueueBackground(
	IMessageQueueService messageQueueService,
	IServiceProvider serviceProvider,
	IEnumerable<ISubscribeRegistration> subscribes,
	ILogger<MessageQueueBackground> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var subscriptions = new List<IDisposable>();

		foreach (var registration in subscribes)
			subscriptions.Add(
				await registration.SubscribeAsync(
					messageQueueService,
					serviceProvider,
					logger,
					stoppingToken).ConfigureAwait(false));

		stoppingToken.Register(() =>
		{
			foreach (var sub in subscriptions)
				sub.Dispose();
		});
	}
}
