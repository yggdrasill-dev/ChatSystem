using ChatServer.Models;
using Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client;
using System;

namespace ChatServer
{
	class Program
	{
		static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureServices(services =>
				{
					services
						.AddMessageQueue(config =>
						{
							config
								.AddHandler<ChatSendHandler>("chat.send", "chat.send")
								.AddHandler<PlayerListHandler>("chat.player.list", "chat.player.list")
								.AddHandler<RoomListHandler>("chat.room.list", "chat.room.list");
						})
						.AddTransient<IQueryService<PlayerInfoQuery, PlayerInfo>, PlayerInfoQueryService>()
						.AddTransient<IQueryService<ListPlayerQuery, string>, ListPlayerQueryService>()
						.AddTransient<ICommandService<LeaveRoomCommand>, LeaveRoomCommandService>()
						.AddTransient<IQueryService<ListRoomQuery, string>, ListRoomQueryService>();
				});
	}
}
