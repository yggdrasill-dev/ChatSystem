using System;
using Common;
using Common.Configuration;
using NATS.Client;

namespace Microsoft.Extensions.DependencyInjection
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddMessageQueue(this IServiceCollection services, Action<MessageQueueConfiguration> configure)
		{
			var configuration = new MessageQueueConfiguration(services);

			configure(configuration);

			return services
				.AddSingleton<ConnectionFactory>()
				.AddSingleton<IMessageQueueService, MessageQueueService>()
				.AddHostedService<MessageQueueBackground>();
		}
	}
}
