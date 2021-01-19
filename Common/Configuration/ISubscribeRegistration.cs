using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Common.Configuration
{
	internal interface ISubscribeRegistration
	{
		ValueTask<IDisposable> SubscribeAsync(
		   IMessageQueueService messageQueueService,
		   IServiceProvider serviceProvider,
		   ILogger logger,
		   CancellationToken cancellationToken);
	}
}
