﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace AuthServer.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
	UserManager<IdentityUser> userManager,
	SignInManager<IdentityUser> signInManager,
	ILogger<RegisterModel> logger)
	: PageModel
{
	[BindProperty]
	public InputModel Input { get; set; }

	public string ReturnUrl { get; set; }

	public IList<AuthenticationScheme> ExternalLogins { get; set; }

	public class InputModel
	{
		[Required]
		[Display(Name = "Name")]
		public string Name { get; set; }

		[Required]
		[StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
		[DataType(DataType.Password)]
		[Display(Name = "Password")]
		public string Password { get; set; }

		[DataType(DataType.Password)]
		[Display(Name = "Confirm password")]
		[Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }
	}

	public async Task OnGetAsync(string returnUrl = null)
	{
		ReturnUrl = returnUrl;
		ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
	}

	public async Task<IActionResult> OnPostAsync(string returnUrl = null)
	{
		returnUrl ??= Url.Content("~/");
		ExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
		if (ModelState.IsValid)
		{
			var user = new IdentityUser { UserName = Input.Name, Email = $"{Input.Name}@test.com" };
			var result = await userManager.CreateAsync(user, Input.Password);
			if (result.Succeeded)
			{
				logger.LogInformation("User created a new account with password.");

				_ = WebEncoders.Base64UrlEncode(
					Encoding.UTF8.GetBytes(await userManager.GenerateEmailConfirmationTokenAsync(user)));

				await signInManager.SignInAsync(user, isPersistent: false);
				return LocalRedirect(returnUrl);
			}

			foreach (var error in result.Errors)
			{
				ModelState.AddModelError(string.Empty, error.Description);
			}
		}

		// If we got this far, something failed, redisplay form
		return Page();
	}
}
