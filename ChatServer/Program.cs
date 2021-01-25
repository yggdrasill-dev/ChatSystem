using ChatServer.Models;
using ChatServer.Models.Endpoints;
using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ChatServer
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
								.AddHandler<ChatSendHandler>("chat.send", "chat.send");

							config.ConfigQueueOptions((options, sp) =>
							{
								var configuration = sp.GetRequiredService<IConfiguration>();

								configuration.GetSection("MessageQueue").Bind(options);
							});
						})
						.AddTransient<IGetService<PlayerInfoQuery, PlayerInfo?>, PlayerInfoQueryService>()
						.AddTransient<IQueryService<ListPlayerQuery, PlayerInfo>, ListPlayerQueryService>()
						.AddTransient<IGetService<GetRoomBySessionidQuery, string?>, GetRoomBySessionIdQueryService>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
