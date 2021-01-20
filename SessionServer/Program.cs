using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SessionServer.Models;
using SessionServer.Models.Handlers;
using StackExchange.Redis;

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
								.AddHandler<GetSessionHandler>("session.get", "session.get")
								.AddHandler<ActiveSessionHandler>("session.active", "session.active");
						})
						.AddSingleton(sp => ConnectionMultiplexer.Connect("localhost:6379"))
						.AddSingleton(sp => sp.GetRequiredService<ConnectionMultiplexer>().GetDatabase())
						.AddSingleton<ISessionRepository, SessionRepository>()
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
