using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(AuthServer.Areas.Identity.IdentityHostingStartup))]

namespace AuthServer.Areas.Identity;

public class IdentityHostingStartup : IHostingStartup
{
	public void Configure(IWebHostBuilder builder)
	{
		builder.ConfigureServices((context, services) =>
		{
		});
	}
}
