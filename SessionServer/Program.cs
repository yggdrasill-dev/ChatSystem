using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using STAN.Client;
using NATS.Client;

namespace SessionServer
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
						.AddSingleton<ConnectionFactory>()
						.AddHostedService<MessageBackground>();
				});
	}
}
