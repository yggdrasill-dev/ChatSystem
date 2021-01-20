using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RoomServer.Models;
using RoomServer.Models.Endpoints;
using RoomServer.Models.Handlers;

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
								.AddHandler<QuerySessionsByRoomHandler>("room.query", "room.query")
								.AddHandler<PlayerListHandler>("room.player.list", "room.player.list")
								.AddHandler<RoomListHandler>("room.list", "room.list")
								.AddHandler<GetRoomBySessionIdHandler>("room.session.get", "room.session.get");
						})
						.AddSingleton<RoomRepository>()
						.AddTransient<ICommandService<JoinRoomCommand>, JoinRoomCommandService>()
						.AddTransient<ICommandService<LeaveRoomCommand>, LeaveRoomCommandService>()
						.AddTransient<IQueryService<RoomSessionsQuery, string>, RoomSessionsQueryService>()
						.AddTransient<IQueryService<RoomListQuery, string>, RoomListQueryService>()
						.AddTransient<IQueryService<PlayerInfoQuery, PlayerInfo>, PlayerInfoQueryService>()
						.AddTransient<IGetService<GetRoomBySessionIdQuery, string?>, GetRoomBySessionIdQueryService>();
				});

		private static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}
	}
}
