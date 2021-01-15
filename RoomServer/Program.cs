using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoomServer.Models;

namespace RoomServer
{
	internal class Program
	{
		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureServices(services =>
				{
					services
						.AddMessageQueue()
						.AddSingleton<RoomRepository>()
						.AddTransient<ICommandService<JoinRoomCommand>, JoinRoomCommandService>()
						.AddTransient<ICommandService<LeaveRoomCommand>, LeaveRoomCommandService>()
						.AddTransient<IQueryService<RoomSessionsQuery, string>, RoomSessionsQueryService>()
						.AddHostedService<MessageBackground>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
