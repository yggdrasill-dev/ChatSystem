using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AuthServer.Helpers;
using AuthServer.ViewModels.Authorization;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace AuthServer.Controllers
{
	public class ConnectController : Controller
	{
		private readonly IOpenIddictApplicationManager m_ApplicationManager;
		private readonly IOpenIddictAuthorizationManager m_AuthorizationManager;
		private readonly IOpenIddictScopeManager m_ScopeManager;
		private readonly SignInManager<IdentityUser> m_SignInManager;
		private readonly UserManager<IdentityUser> m_UserManager;

		public ConnectController(
			IOpenIddictApplicationManager applicationManager,
			IOpenIddictAuthorizationManager authorizationManager,
			IOpenIddictScopeManager scopeManager,
			SignInManager<IdentityUser> signInManager,
			UserManager<IdentityUser> userManager)
		{
			m_ApplicationManager = applicationManager ?? throw new ArgumentNullException(nameof(applicationManager));
			m_AuthorizationManager = authorizationManager ?? throw new ArgumentNullException(nameof(authorizationManager));
			m_ScopeManager = scopeManager ?? throw new ArgumentNullException(nameof(scopeManager));
			m_SignInManager = signInManager;
			m_UserManager = userManager;
		}

		// Note: to support interactive flows like the code flow,
		// you must provide your own authorization endpoint action:

		[HttpGet("~/connect/authorize")]
		[HttpPost("~/connect/authorize")]
		//[IgnoreAntiforgeryToken]
		public async Task<IActionResult> Authorize()
		{
			var request = HttpContext.GetOpenIddictServerRequest() ??
				throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

			// Retrieve the user principal stored in the authentication cookie.
			// If it can't be extracted, redirect the user to the login page.
			var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
			if (result is null || !result.Succeeded)
			{
				// If the client application requested promptless authentication,
				// return an error indicating that the user is not logged in.
				if (request.HasPrompt(Prompts.None))
				{
					return Forbid(
						authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
						properties: new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
						}));
				}

				return Challenge(
					authenticationSchemes: IdentityConstants.ApplicationScheme,
					properties: new AuthenticationProperties
					{
						RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
							Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
					});
			}

			// If prompt=login was specified by the client application,
			// immediately return the user agent to the login page.
			if (request.HasPrompt(Prompts.Login))
			{
				// To avoid endless login -> authorization redirects, the prompt=login flag
				// is removed from the authorization request payload before redirecting the user.
				var prompt = string.Join(" ", request.GetPrompts().Remove(Prompts.Login));

				var parameters = Request.HasFormContentType ?
					Request.Form.Where(parameter => parameter.Key != Parameters.Prompt).ToList() :
					Request.Query.Where(parameter => parameter.Key != Parameters.Prompt).ToList();

				parameters.Add(KeyValuePair.Create(Parameters.Prompt, new StringValues(prompt)));

				return Challenge(
					authenticationSchemes: IdentityConstants.ApplicationScheme,
					properties: new AuthenticationProperties
					{
						RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters)
					});
			}

			// If a max_age parameter was provided, ensure that the cookie is not too old.
			// If it's too old, automatically redirect the user agent to the login page.
			if (request.MaxAge is not null && result.Properties?.IssuedUtc is not null &&
				DateTimeOffset.UtcNow - result.Properties.IssuedUtc > TimeSpan.FromSeconds(request.MaxAge.Value))
			{
				if (request.HasPrompt(Prompts.None))
				{
					return Forbid(
						authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
						properties: new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
						}));
				}

				return Challenge(
					authenticationSchemes: IdentityConstants.ApplicationScheme,
					properties: new AuthenticationProperties
					{
						RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
							Request.HasFormContentType ? Request.Form.ToList() : Request.Query.ToList())
					});
			}

			// Retrieve the profile of the logged in user.
			var user = await m_UserManager.GetUserAsync(result.Principal) ??
				throw new InvalidOperationException("The user details cannot be retrieved.");

			// Retrieve the application details from the database.
			var application = await m_ApplicationManager.FindByClientIdAsync(request.ClientId) ??
				throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

			// Retrieve the permanent authorizations associated with the user and the calling client application.
			var authorizations = await m_AuthorizationManager.FindAsync(
				subject: await m_UserManager.GetUserIdAsync(user),
				client: await m_ApplicationManager.GetIdAsync(application),
				status: Statuses.Valid,
				type: AuthorizationTypes.Permanent,
				scopes: request.GetScopes()).ToListAsync();

			switch (await m_ApplicationManager.GetConsentTypeAsync(application))
			{
				// If the consent is external (e.g when authorizations are granted by a sysadmin),
				// immediately return an error if no authorization can be found in the database.
				case ConsentTypes.External when !authorizations.Any():
					return Forbid(
						authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
						properties: new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
								"The logged in user is not allowed to access this client application."
						}));

				// If the consent is implicit or if an authorization was found,
				// return an authorization response without displaying the consent form.
				case ConsentTypes.Implicit:
				case ConsentTypes.External when authorizations.Any():
				case ConsentTypes.Explicit when authorizations.Any() && !request.HasPrompt(Prompts.Consent):
					var principal = await m_SignInManager.CreateUserPrincipalAsync(user);

					// Note: in this sample, the granted scopes match the requested scope
					// but you may want to allow the user to uncheck specific scopes.
					// For that, simply restrict the list of scopes before calling SetScopes.
					principal.SetScopes(request.GetScopes());
					principal.SetResources(await m_ScopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

					// Automatically create a permanent authorization to avoid requiring explicit consent
					// for future authorization or token requests containing the same scopes.
					var authorization = authorizations.LastOrDefault();
					if (authorization is null)
					{
						authorization = await m_AuthorizationManager.CreateAsync(
							principal: principal,
							subject: await m_UserManager.GetUserIdAsync(user),
							client: await m_ApplicationManager.GetIdAsync(application),
							type: AuthorizationTypes.Permanent,
							scopes: principal.GetScopes());
					}

					principal.SetAuthorizationId(await m_AuthorizationManager.GetIdAsync(authorization));

					foreach (var claim in principal.Claims)
					{
						claim.SetDestinations(GetDestinations(claim, principal));
					}

					return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

				// At this point, no authorization was found in the database and an error must be returned
				// if the client application specified prompt=none in the authorization request.
				case ConsentTypes.Explicit when request.HasPrompt(Prompts.None):
				case ConsentTypes.Systematic when request.HasPrompt(Prompts.None):
					return Forbid(
						authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
						properties: new AuthenticationProperties(new Dictionary<string, string>
						{
							[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
							[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
								"Interactive user consent is required."
						}));

				// In every other case, render the consent form.
				default:
					return View(new AuthorizeViewModel
					{
						ApplicationName = await m_ApplicationManager.GetLocalizedDisplayNameAsync(application),
						Scope = request.Scope
					});
			}
		}

		[Authorize, FormValueRequired("submit.Accept")]
		[HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
		public async Task<IActionResult> Accept()
		{
			var request = HttpContext.GetOpenIddictServerRequest() ??
				throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

			// Retrieve the profile of the logged in user.
			var user = await m_UserManager.GetUserAsync(User) ??
				throw new InvalidOperationException("The user details cannot be retrieved.");

			// Retrieve the application details from the database.
			var application = await m_ApplicationManager.FindByClientIdAsync(request.ClientId) ??
				throw new InvalidOperationException("Details concerning the calling client application cannot be found.");

			// Retrieve the permanent authorizations associated with the user and the calling client application.
			var authorizations = await m_AuthorizationManager.FindAsync(
				subject: await m_UserManager.GetUserIdAsync(user),
				client: await m_ApplicationManager.GetIdAsync(application),
				status: Statuses.Valid,
				type: AuthorizationTypes.Permanent,
				scopes: request.GetScopes()).ToListAsync();

			// Note: the same check is already made in the other action but is repeated
			// here to ensure a malicious user can't abuse this POST-only endpoint and
			// force it to return a valid response without the external authorization.
			if (!authorizations.Any() && await m_ApplicationManager.HasConsentTypeAsync(application, ConsentTypes.External))
			{
				return Forbid(
					authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
					properties: new AuthenticationProperties(new Dictionary<string, string>
					{
						[OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.ConsentRequired,
						[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
							"The logged in user is not allowed to access this client application."
					}));
			}

			var principal = await m_SignInManager.CreateUserPrincipalAsync(user);

			// Note: in this sample, the granted scopes match the requested scope
			// but you may want to allow the user to uncheck specific scopes.
			// For that, simply restrict the list of scopes before calling SetScopes.
			principal.SetScopes(request.GetScopes());
			principal.SetResources(await m_ScopeManager.ListResourcesAsync(principal.GetScopes()).ToListAsync());

			// Automatically create a permanent authorization to avoid requiring explicit consent
			// for future authorization or token requests containing the same scopes.
			var authorization = authorizations.LastOrDefault();
			if (authorization is null)
			{
				authorization = await m_AuthorizationManager.CreateAsync(
					principal: principal,
					subject: await m_UserManager.GetUserIdAsync(user),
					client: await m_ApplicationManager.GetIdAsync(application),
					type: AuthorizationTypes.Permanent,
					scopes: principal.GetScopes());
			}

			principal.SetAuthorizationId(await m_AuthorizationManager.GetIdAsync(authorization));

			foreach (var claim in principal.Claims)
			{
				claim.SetDestinations(GetDestinations(claim, principal));
			}

			// Returning a SignInResult will ask OpenIddict to issue the appropriate access/identity tokens.
			return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		}

		[Authorize, FormValueRequired("submit.Deny")]
		[HttpPost("~/connect/authorize"), ValidateAntiForgeryToken]
		// Notify OpenIddict that the authorization grant has been denied by the resource owner
		// to redirect the user agent to the client application using the appropriate response_mode.
		public IActionResult Deny() => Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

		private IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
		{
			// Note: by default, claims are NOT automatically included in the access and identity tokens.
			// To allow OpenIddict to serialize them, you must attach them a destination, that specifies
			// whether they should be included in access tokens, in identity tokens or in both.

			switch (claim.Type)
			{
				case Claims.Name:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Profile))
						yield return Destinations.IdentityToken;

					yield break;

				case Claims.Email:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Email))
						yield return Destinations.IdentityToken;

					yield break;

				case Claims.Role:
					yield return Destinations.AccessToken;

					if (principal.HasScope(Scopes.Roles))
						yield return Destinations.IdentityToken;

					yield break;

				// Never include the security stamp in the access and identity tokens, as it's a secret value.
				case "AspNet.Identity.SecurityStamp": yield break;

				default:
					yield return Destinations.AccessToken;
					yield break;
			}
		}
	}
}
