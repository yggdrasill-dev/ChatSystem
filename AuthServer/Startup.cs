using AuthServer.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace AuthServer
{
	public class Startup
	{
		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddDbContext<ApplicationDbContext>((sp, options) =>
			{
				var configuration = sp.GetRequiredService<IConfiguration>();

				options
					.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
					.UseOpenIddict();
			});
			services.AddDatabaseDeveloperPageExceptionFilter();
			services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
				.AddEntityFrameworkStores<ApplicationDbContext>()
				.AddDefaultTokenProviders();

			services.AddControllersWithViews();

			services.Configure<IdentityOptions>(options =>
			{
				options.ClaimsIdentity.UserNameClaimType = Claims.Name;
				options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
				options.ClaimsIdentity.RoleClaimType = Claims.Role;
			});

			services.AddOpenIddict()
				.AddCore(builder =>
				{
					builder.UseEntityFrameworkCore()
						.UseDbContext<ApplicationDbContext>();
				})
				.AddServer(builder =>
				{
					builder
						.SetAuthorizationEndpointUris("/connect/authorize")
						//.SetDeviceEndpointUris("/connect/device")
						.SetLogoutEndpointUris("/connect/logout")
						.SetTokenEndpointUris("/connect/token");
					//.SetUserinfoEndpointUris("/connect/userinfo")
					//.SetVerificationEndpointUris("/connect/verify");

					builder
						.AllowAuthorizationCodeFlow()
						//.AllowDeviceCodeFlow()
						//.AllowPasswordFlow()
						.AllowRefreshTokenFlow();

					builder.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, "demo_api");

					builder
						.AddDevelopmentEncryptionCertificate()
						.AddDevelopmentSigningCertificate();

					builder
						.RequireProofKeyForCodeExchange();

					builder.UseAspNetCore()
						.EnableAuthorizationEndpointPassthrough();
				});

			services.AddHostedService<Worker>();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				app.UseMigrationsEndPoint();
			}
			else
			{
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			app.UseHttpsRedirection();
			app.UseStaticFiles();

			app.UseRouting();

			app.UseAuthentication();
			app.UseAuthorization();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapRazorPages();
				endpoints.MapControllers();
			});
		}
	}
}
