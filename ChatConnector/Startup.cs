using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using ChatConnector.Models;
using Common;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NATS.Client;
using Quartz;

namespace ChatConnector
{
	public class Startup
	{
		private readonly Guid m_ConnectorId = Guid.NewGuid();

		// This method gets called by the runtime. Use this method to add services to the container.
		// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSingleton<ConnectionFactory>();

			services.AddAuthentication(options =>
			{
				options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
			})

			.AddCookie(options =>
			{
				options.LoginPath = "/login";
			})

			.AddOpenIdConnect(options =>
			{
				// Note: these settings must match the application details
				// inserted in the database at the server level.
				options.ClientId = "mvc";
				options.ClientSecret = "901564A5-E7FE-42CB-B10D-61EF6A8F3654";

				options.RequireHttpsMetadata = false;
				options.GetClaimsFromUserInfoEndpoint = true;
				options.SaveTokens = true;

				// Use the authorization code flow.
				options.ResponseType = OpenIdConnectResponseType.Code;
				options.AuthenticationMethod = OpenIdConnectRedirectBehavior.RedirectGet;

				// Note: setting the Authority allows the OIDC client middleware to automatically
				// retrieve the identity provider's configuration and spare you from setting
				// the different endpoints URIs or the token validation parameters explicitly.
				options.Authority = "https://localhost:5001/";

				options.Scope.Add("email");
				options.Scope.Add("roles");
				options.Scope.Add("offline_access");
				options.Scope.Add("demo_api");

				options.SecurityTokenValidator = new JwtSecurityTokenHandler
				{
					// Disable the built-in JWT claims mapping feature.
					InboundClaimTypeMap = new Dictionary<string, string>()
				};

				options.TokenValidationParameters.NameClaimType = "name";
				options.TokenValidationParameters.RoleClaimType = "role";

				options.AccessDeniedPath = "/";
			});

			services.AddRazorPages(options =>
			{
				options.Conventions.AuthorizeFolder("/");
			});
			services.AddControllersWithViews();

			services
				.AddTransient<ICommandService<RegisterSessionCommand>, RegisterSessionCommandService>()
				.AddTransient<ICommandService<AddSocketCommand>, AddSocketCommandService>()
				.AddTransient<ICommandService<SendQueueCommand>, SendQueueCommandService>()
				.AddTransient<ICommandService<JoinRoomCommand>, JoinRoomCommandService>()
				.AddTransient<ICommandService<UnregisterSessionCommand>, UnregisterSessionCommandService>()
				.AddTransient<ICommandService<LeaveRoomCommand>, LeaveRoomCommandService>()
				.AddTransient<ICommandService<RemoveSocketCommand>, RemoveSocketCommandService>()
				.AddSingleton<WebSocketRepository>()
				.AddMessageQueue(config =>
				{
					config
						.AddHandler<ConnectSendHandler>("connect.send")
						.AddHandler<ConnectSendHandler>($"connect.send.{m_ConnectorId:N}");
				})
				.AddQuartz(config =>
				{
					config.UseMicrosoftDependencyInjectionScopedJobFactory();
					config.ScheduleJob<ActiveSessionsJob>(trigger => trigger
						.WithIdentity("ActiveSessions")
						.StartAt(DateTimeOffset.UtcNow.AddSeconds(10D))
						.WithDailyTimeIntervalSchedule(x => x.WithIntervalInSeconds(10)));
				})
				.AddQuartzServer(options =>
				{
					options.WaitForJobsToComplete = false;
				});
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			app.UseStaticFiles();
			app.UseWebSockets();
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();
			app.UseEndpoints(endpoints =>
			{
				endpoints.MapWebSocketManager<ClientConnectHandler>("/ws", m_ConnectorId.ToString("N"));
				endpoints.MapControllers();
				endpoints.MapRazorPages();
			});
		}
	}
}
