using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client;
using SessionServer.Models;

namespace SessionServer
{
	internal class Program
	{
		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureServices(services =>
				{
					services
						.AddSingleton<ConnectionFactory>()
						.AddSingleton<SessionRepository>()
						.AddTransient<ICommandService<RegisterCommand>, RegisterSessionService>()
						.AddHostedService<MessageBackground>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
