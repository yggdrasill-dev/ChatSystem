using System;
using Common;
using Common.Configuration;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddMessageQueue(
		this IServiceCollection services,
		Action<MessageQueueConfiguration> configure)
	{
		var configuration = new MessageQueueConfiguration(services);

		configure(configuration);

		return services
			.AddSingleton<ConnectionFactory>()
			.AddSingleton<IMessageQueueService>(sp =>
			{
				var connectionFactory = sp.GetRequiredService<ConnectionFactory>();
				var queueOptions = sp.GetRequiredService<IOptions<MessageQueueOptions>>().Value;

				var options = ConnectionFactory.GetDefaultOptions();

				options.Url = queueOptions.Url;

				return ActivatorUtilities.CreateInstance<MessageQueueService>(sp, connectionFactory.CreateConnection(options));
			})
			.AddHostedService<MessageQueueBackground>()
			.AddOptions<MessageQueueOptions>()
			.Configure(configuration.OptionsConfigure)
			.Services;
	}
}
