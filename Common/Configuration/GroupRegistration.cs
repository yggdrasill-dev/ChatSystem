using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Configuration;

record GroupRegistration<THandler>(string Subject, string Group) : ISubscribeRegistration where THandler : IMessageHandler
{
	public ValueTask<IDisposable> SubscribeAsync(
		IMessageQueueService messageQueueService,
		IServiceProvider serviceProvider,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		return messageQueueService.SubscribeAsync(Subject, async (sender, args) =>
		{
			try
			{
				using var scope = serviceProvider.CreateScope();
				var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);

				await handler.HandleAsync(args.Message, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Handle {Subject} occur error.", Subject);
			}
		});
	}
}
