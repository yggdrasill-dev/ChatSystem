using Common;
using NATS.Client;

namespace Microsoft.Extensions.DependencyInjection
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddMessageQueue(this IServiceCollection services) =>
			services
				.AddSingleton<ConnectionFactory>()
				.AddSingleton<IMessageQueueService, MessageQueueService>();
	}
}
