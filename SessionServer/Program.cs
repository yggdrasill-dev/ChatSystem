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
						.AddMessageQueue(config =>
						{
							config
								.AddHandler<RegisterSessionHandler>("session.register", "session.register")
								.AddHandler<UnregisterSessionHandler>("session.unregister", "session.unregister")
								.AddHandler<GetSessionHandler>("session.get", "session.get");
						})
						.AddSingleton<SessionRepository>()
						.AddTransient<ICommandService<RegisterCommand>, RegisterSessionService>()
						.AddTransient<ICommandService<UnregisterSessionCommand>, UnregisterSessionCommandService>()
						.AddTransient<IGetService<GetPlayerBySessionIdQuery, Registration?>, GetPlayerBySessionIdService>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
