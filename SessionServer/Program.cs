using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
						.AddMessageQueue()
						.AddSingleton<SessionRepository>()
						.AddTransient<ICommandService<RegisterCommand>, RegisterSessionService>()
						.AddTransient<IGetService<GetPlayerBySessionIdQuery, Registration?>, GetPlayerBySessionIdService>()
						.AddHostedService<MessageBackground>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
