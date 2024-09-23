using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AuthServer.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer;

public class Worker(
	IServiceProvider serviceProvider)
	: IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		using var scope = serviceProvider.CreateScope();

		var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

		await context.Database.EnsureCreatedAsync(cancellationToken);

		await RegisterApplicationsAsync(scope.ServiceProvider);
		await RegisterScopesAsync(scope.ServiceProvider);

		async Task RegisterApplicationsAsync(IServiceProvider provider)
		{
			var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

			if (await manager.FindByClientIdAsync("mvc", cancellationToken) is not null)
				return;

			await manager.CreateAsync(
				new OpenIddictApplicationDescriptor
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
						new Uri("https://localhost:5002/signout-callback-oidc"),
						new Uri("https://localhost:17002/signout-callback-oidc"),
						new Uri("https://chat.test.com/signout-callback-oidc")
					},
					RedirectUris =
					{
						new Uri("https://localhost:5002/signin-oidc"),
						new Uri("https://localhost:17002/signin-oidc"),
						new Uri("https://chat.test.com/signin-oidc")
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
				},
				cancellationToken);
		}

		async Task RegisterScopesAsync(IServiceProvider provider)
		{
			var manager = provider.GetRequiredService<IOpenIddictScopeManager>();

			if (await manager.FindByNameAsync("demo_api", cancellationToken) is not null)
				return;

			await manager.CreateAsync(
				new OpenIddictScopeDescriptor
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
				},
				cancellationToken);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
