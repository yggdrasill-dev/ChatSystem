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
						.AddMessageQueue()
						.AddTransient<IQueryService<GetPlayerQuery, PlayerInfo>, GetPlayerInfoService>()
						.AddTransient<IQueryService<RoomListQuery, string>, RoomListQueryService>()
						.AddHostedService<MessageBackground>();
				});
	}
}
