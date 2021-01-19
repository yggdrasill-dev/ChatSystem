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
						.AddMessageQueue(config =>
						{
							config
								.AddHandler<JoinRoomHandler>("room.join", "room.join")
								.AddHandler<LeaveRoomHandler>("room.leave", "room.leave")
								.AddHandler<QueryRoomHandler>("room.query", "room.query");
						})
						.AddSingleton<RoomRepository>()
						.AddTransient<ICommandService<JoinRoomCommand>, JoinRoomCommandService>()
						.AddTransient<ICommandService<LeaveRoomCommand>, LeaveRoomCommandService>()
						.AddTransient<IQueryService<RoomSessionsQuery, string>, RoomSessionsQueryService>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
