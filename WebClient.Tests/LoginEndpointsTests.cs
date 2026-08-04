using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Common.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WebClient.Services;

namespace WebClient.Tests;

// 對著真的 Kestrel 跑：要驗的東西有一半在 HTTP 層（狀態碼、Set-Cookie 的屬性），
// 用 in-memory 的 handler 測不到 cookie 真的長什麼樣。
public class LoginEndpointsTests
{
	private const string SessionCookieName = "chat_session";

	[Fact]
	public async Task IssueNonce_ReturnsTheNonceFromTheStore()
	{
		await using var host = await LoginHost.StartAsync();
		host.Nonces.IssueAsync(Arg.Any<CancellationToken>()).Returns("nonce-1");

		var response = await host.Client.GetAsync("/login/nonce");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("nonce-1", (await response.Content.ReadFromJsonAsync<NonceBody>())?.Nonce);
		host.AssertNoServerErrors();
	}

	[Fact]
	public async Task Login_Returns503_WhenGoogleSignInIsNotConfigured()
	{
		await using var host = await LoginHost.StartAsync();
		host.Validator.IsConfigured.Returns(false);

		var response = await host.PostLoginAsync("token", "nonce-1");

		// 漏設定 client id 的症狀刻意限縮在這個 endpoint，而不是讓整個 process 起不來
		Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Empty(host.Sessions.ReceivedCalls());
	}

	[Fact]
	public async Task Login_Returns401_WhenTheNonceWasAlreadyConsumed()
	{
		await using var host = await LoginHost.StartAsync();
		host.Nonces.TryConsumeAsync("nonce-1", Arg.Any<CancellationToken>()).Returns(false);

		var response = await host.PostLoginAsync("token", "nonce-1");

		// 重放同一個 nonce 一定失敗，而且不該白白去驗一次 token
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		await host.Validator.DidNotReceive().ValidateAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Login_ConsumesTheNonce_BeforeValidatingTheToken()
	{
		await using var host = await LoginHost.StartAsync();
		host.Nonces.TryConsumeAsync("nonce-1", Arg.Any<CancellationToken>()).Returns(true);
		host.Validator
			.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((IdTokenPayload?)null);

		var response = await host.PostLoginAsync("bad-token", "nonce-1");

		// 順序反過來（先驗再消耗）的話，兩個併發請求會同時通過驗證，nonce 等於沒防；
		// 而且驗證失敗也已經燒掉一個 nonce 是正確的——它是一次性的，不論結果
		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Received.InOrder(() =>
		{
			host.Nonces.TryConsumeAsync("nonce-1", Arg.Any<CancellationToken>());
			host.Validator.ValidateAsync("bad-token", "nonce-1", Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task Login_CreatesTheSession_AndSetsAnHttpOnlySecureLaxCookie()
	{
		await using var host = await LoginHost.StartAsync();
		host.StubSuccessfulLogin("user-1", "Sunny", "https://example/pic");
		host.Sessions.CreateSessionAsync("user-1", Arg.Any<CancellationToken>()).Returns("token-1");

		var response = await host.PostLoginAsync("id-token", "nonce-1");

		Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

		var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
		Assert.Contains($"{SessionCookieName}=token-1", cookie);
		Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
		host.AssertNoServerErrors();
	}

	[Fact]
	public async Task Login_SavesTheProfile_BecauseTheIdTokenIsTheOnlyPlaceItEverAppears()
	{
		await using var host = await LoginHost.StartAsync();
		host.StubSuccessfulLogin("user-1", "Sunny", "https://example/pic");

		await host.PostLoginAsync("id-token", "nonce-1");

		await host.Profiles.Received(1).SaveAsync(
			"user-1",
			"Sunny",
			"https://example/pic",
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetSession_Returns401_WhenThereIsNoCookie()
	{
		await using var host = await LoginHost.StartAsync();

		var response = await host.Client.GetAsync("/login/session");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		await host.Sessions.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetSession_Returns401_WhenTheSessionIsGone()
	{
		await using var host = await LoginHost.StartAsync();
		host.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>()).Returns((string?)null);

		var response = await host.GetSessionAsync("token-1");

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
	}

	[Fact]
	public async Task GetSession_SlidesTheSession_AndReissuesTheCookie()
	{
		await using var host = await LoginHost.StartAsync();
		host.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>()).Returns("user-1");

		var response = await host.GetSessionAsync("token-1");

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal("user-1", (await response.Content.ReadFromJsonAsync<SessionBody>())?.UserId);
		await host.Sessions.Received(1).RefreshAsync("token-1", Arg.Any<CancellationToken>());

		// cookie 也要重簽：Session 隨活動 sliding，cookie 的 MaxAge 不會，
		// 少了這一步「登入滿 7 天就被登出」會蓋掉 sliding 續期的意義
		var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
		Assert.Contains($"{SessionCookieName}=token-1", cookie);
		Assert.Contains("max-age", cookie, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Logout_ResolvesTheUserBeforeRevoking_ThenTerminatesTheConnections()
	{
		await using var host = await LoginHost.StartAsync();
		host.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>()).Returns("user-1");

		var response = await host.PostLogoutAsync("token-1");

		Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

		// 順序不能反：撤銷之後就查不到這個 token 屬於誰，連線也就踢不掉。
		// 少了 TerminateConnectionsAsync，「登出」之後那條 WebSocket 還是活的。
		Received.InOrder(() =>
		{
			host.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>());
			host.Sessions.RevokeAsync("token-1", Arg.Any<CancellationToken>());
			host.Authenticator.TerminateConnectionsAsync("user-1", Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task Logout_ClearsTheCookie_EvenWhenTheSessionWasAlreadyInvalid()
	{
		await using var host = await LoginHost.StartAsync();
		host.Sessions.ResolveUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

		var response = await host.PostLogoutAsync("stale-token");

		// 回應不因 token 是否有效而不同：呼叫端無法從這裡探測 session 的有效性
		Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
		Assert.Contains($"{SessionCookieName}=", Assert.Single(response.Headers.GetValues("Set-Cookie")));
		await host.Authenticator.DidNotReceive().TerminateConnectionsAsync(
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	private sealed record NonceBody(string Nonce);

	private sealed record SessionBody(string UserId);

	private sealed class LoginHost : IAsyncDisposable
	{
		private readonly ConcurrentBag<string> m_ServerErrors = [];

		private WebApplication m_App = null!;

		public HttpClient Client { get; private set; } = null!;

		public IIdTokenValidator Validator { get; } = Substitute.For<IIdTokenValidator>();

		public ILoginNonceStore Nonces { get; } = Substitute.For<ILoginNonceStore>();

		public ISessionStore Sessions { get; } = Substitute.For<ISessionStore>();

		public IUserProfileStore Profiles { get; } = Substitute.For<IUserProfileStore>();

		public IConnectionAuthenticator Authenticator { get; } = Substitute.For<IConnectionAuthenticator>();

		public static async Task<LoginHost> StartAsync()
		{
			var host = new LoginHost();
			await host.StartCoreAsync();

			return host;
		}

		public void StubSuccessfulLogin(string userId, string? displayName, string? pictureUrl)
		{
			Nonces.TryConsumeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
			Validator
				.ValidateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
				.Returns(new IdTokenPayload(userId, displayName, pictureUrl));
		}

		public Task<HttpResponseMessage> PostLoginAsync(string idToken, string nonce) =>
			Client.PostAsJsonAsync("/login", new { idToken, nonce });

		public Task<HttpResponseMessage> GetSessionAsync(string sessionToken) =>
			SendWithCookieAsync(HttpMethod.Get, "/login/session", sessionToken);

		public Task<HttpResponseMessage> PostLogoutAsync(string sessionToken) =>
			SendWithCookieAsync(HttpMethod.Post, "/logout", sessionToken);

		// 沒有這個斷言的話，「endpoint 自己炸掉」會偽裝成「狀態碼跟預期不符」，
		// 真正的原因完全看不到（Gateway.Tests 踩過一次這個坑）。
		public void AssertNoServerErrors() => Assert.Empty(m_ServerErrors);

		public async ValueTask DisposeAsync()
		{
			Client.Dispose();
			await m_App.StopAsync();
			await m_App.DisposeAsync();
		}

		private Task<HttpResponseMessage> SendWithCookieAsync(HttpMethod method, string path, string sessionToken)
		{
			var request = new HttpRequestMessage(method, path);
			request.Headers.Add("Cookie", $"{SessionCookieName}={sessionToken}");

			return Client.SendAsync(request);
		}

		private async Task StartCoreAsync()
		{
			// 大多數測試都預期「已設定 client id」，只有 503 那個會覆寫掉
			Validator.IsConfigured.Returns(true);

			var builder = WebApplication.CreateBuilder();
			builder.Logging.ClearProviders();
			builder.Logging.AddProvider(new CapturingLoggerProvider(m_ServerErrors));
			builder.WebHost.UseUrls("http://127.0.0.1:0");

			builder.Services.AddSingleton(Validator);
			builder.Services.AddSingleton(Nonces);
			builder.Services.AddSingleton(Sessions);
			builder.Services.AddSingleton(Profiles);
			builder.Services.AddSingleton(Authenticator);

			m_App = builder.Build();
			m_App.MapLoginEndpoints();

			await m_App.StartAsync();

			var addresses = m_App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;

			// UseCookies = false 才看得到原始的 Set-Cookie header
			Client = new HttpClient(new HttpClientHandler { UseCookies = false })
			{
				BaseAddress = new Uri(addresses.Addresses.First())
			};
		}
	}

	private sealed class CapturingLoggerProvider(ConcurrentBag<string> errors) : ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName) => new CapturingLogger(errors);

		public void Dispose()
		{
		}

		private sealed class CapturingLogger(ConcurrentBag<string> errors) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

			public void Log<TState>(
				LogLevel logLevel,
				EventId eventId,
				TState state,
				Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				if (logLevel >= LogLevel.Error)
					errors.Add($"{formatter(state, exception)}{Environment.NewLine}{exception}");
			}
		}
	}
}
