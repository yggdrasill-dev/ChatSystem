using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SessionServer.Models;
using SessionServer.Models.Handlers;
using StackExchange.Redis;

namespace SessionServer;

internal class Program
{
	public static IHostBuilder CreateHostBuilder(string[] args) =>
		Host.CreateDefaultBuilder(args)
			.ConfigureServices(services => services
				.AddMessageQueue(config =>
				{
					config
						.AddHandler<RegisterSessionHandler>("session.register", "session.register")
						.AddHandler<UnregisterSessionHandler>("session.unregister", "session.unregister")
						.AddHandler<GetSessionHandler>("session.get", "session.get")
						.AddHandler<ActiveSessionHandler>("session.active", "session.active");

					config.ConfigQueueOptions((options, sp) =>
					{
						var configuration = sp.GetRequiredService<IConfiguration>();

						configuration.GetSection("MessageQueue").Bind(options);
					});
				})
				.AddSingleton(sp =>
				{
					var config = sp.GetRequiredService<IConfiguration>();

					return ConnectionMultiplexer.Connect(config.GetValue<string>("Redis:ConnectionString")!);
				})
				.AddSingleton(sp => sp.GetRequiredService<ConnectionMultiplexer>().GetDatabase())
				.AddSingleton<ISessionRepository, SessionRepository>()
				.AddTransient<ICommandService<RegisterCommand>, RegisterSessionService>()
				.AddTransient<ICommandService<UnregisterSessionCommand>, UnregisterSessionCommandService>()
				.AddTransient<IGetService<GetPlayerBySessionIdQuery, Registration?>, GetPlayerBySessionIdService>());

	private static void Main(string[] args)
	{
		CreateHostBuilder(args).Build().Run();
	}
}
