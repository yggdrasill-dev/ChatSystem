using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuthServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer
{
	public class Worker : IHostedService
	{
		private readonly IServiceProvider m_ServiceProvider;
		private readonly ILogger<Worker> m_Logger;

		public Worker(IServiceProvider serviceProvider, ILogger<Worker> logger)
		{
			m_ServiceProvider = serviceProvider;
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			using var scope = m_ServiceProvider.CreateScope();

			var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
			m_Logger.LogInformation(context.Database.GetConnectionString());
			await context.Database.EnsureCreatedAsync(cancellationToken);

			await RegisterApplicationsAsync(scope.ServiceProvider);
			await RegisterScopesAsync(scope.ServiceProvider);

			static async Task RegisterApplicationsAsync(IServiceProvider provider)
			{
				var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

				if (await manager.FindByClientIdAsync("mvc") is null)
				{
					await manager.CreateAsync(new OpenIddictApplicationDescriptor
					{
						ClientId = "mvc",
						ClientSecret = "901564A5-E7FE-42CB-B10D-61EF6A8F3654",
						ConsentType = ConsentTypes.Explicit,
						DisplayName = "MVC client application",
						DisplayNames =
						{
							[CultureInfo.CurrentUICulture] = "MVC Client"
						},
						PostLogoutRedirectUris =
						{
							new Uri("https://localhost:44381/signout-callback-oidc")
						},
						RedirectUris =
						{
							new Uri("https://localhost:44381/signin-oidc")
						},
						Permissions =
						{
							Permissions.Endpoints.Authorization,
							Permissions.Endpoints.Logout,
							Permissions.Endpoints.Token,
							Permissions.GrantTypes.AuthorizationCode,
							Permissions.GrantTypes.RefreshToken,
							Permissions.ResponseTypes.Code,
							Permissions.Scopes.Email,
							Permissions.Scopes.Profile,
							Permissions.Scopes.Roles,
							Permissions.Prefixes.Scope + "demo_api"
						},
						Requirements =
						{
							Requirements.Features.ProofKeyForCodeExchange
						}
					});
				}
			}

			static async Task RegisterScopesAsync(IServiceProvider provider)
			{
				var manager = provider.GetRequiredService<IOpenIddictScopeManager>();

				if (await manager.FindByNameAsync("demo_api") is null)
				{
					await manager.CreateAsync(new OpenIddictScopeDescriptor
					{
						DisplayName = "Demo API access",
						DisplayNames =
						{
							[CultureInfo.GetCultureInfo("fr-FR")] = "Accès à l'API de démo"
						},
						Name = "demo_api",
						Resources =
						{
							"resource_server"
						}
					});
				}
			}
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}
