using Common.Identity;

namespace WebClient.Services;

// 登入流程的 HTTP 入口。跟前端寄宿在同一個 host，所以：
// - 前端的 fetch 是 same-origin，不需要 CORS
// - cookie 由使用者正在看的那個站台簽發，SameSite=Lax 自然成立
// 見 identity-layer.md ADR-10。
internal static class LoginEndpoints
{
	// cookie 的 7 天跟 Session 的 7 天是兩件事：Session 會隨活動 sliding 續期，cookie 不會，
	// 所以 GET /login/session 要順手重新簽一次，否則「登入滿 7 天就被登出」會蓋掉 sliding。
	private static readonly TimeSpan _CookieLifetime = TimeSpan.FromDays(7);

	public static void MapLoginEndpoints(this WebApplication app)
	{
		app.MapGet("/login/nonce", IssueNonceAsync);
		app.MapPost("/login", LoginAsync);
		app.MapGet("/login/session", GetSessionAsync);
		app.MapPost("/logout", LogoutAsync);
	}

	private static async Task<IResult> IssueNonceAsync(ILoginNonceStore nonces, CancellationToken cancellationToken)
	{
		var nonce = await nonces.IssueAsync(cancellationToken).ConfigureAwait(false);

		return Results.Ok(new NonceResponse(nonce));
	}

	private static async Task<IResult> LoginAsync(
		LoginRequest request,
		HttpContext context,
		IIdTokenValidator validator,
		ILoginNonceStore nonces,
		ISessionStore sessions,
		IUserProfileStore profiles,
		CancellationToken cancellationToken)
	{
		if (!validator.IsConfigured)
			return Results.Problem(
				"Google sign-in is not configured on this deployment.",
				statusCode: StatusCodes.Status503ServiceUnavailable);

		if (string.IsNullOrEmpty(request.IdToken) || string.IsNullOrEmpty(request.Nonce))
			return Results.Unauthorized();

		// 順序不能反：先消耗 nonce（DEL 的回傳值是一次性守門），再驗 token。
		// 「先驗 token 再消耗」的話，兩個併發請求會同時通過驗證，nonce 等於沒防。
		// 驗證失敗也已經燒掉一個 nonce 是正確的——nonce 是一次性的，不論結果。
		if (!await nonces.TryConsumeAsync(request.Nonce, cancellationToken).ConfigureAwait(false))
			return Results.Unauthorized();

		var payload = await validator
			.ValidateAsync(request.IdToken, request.Nonce, cancellationToken)
			.ConfigureAwait(false);

		if (payload is null)
			return Results.Unauthorized();

		// 這是整個系統唯一看得到 name / picture 的地方，不寫下來就永遠拿不回來。
		await profiles
			.SaveAsync(payload.UserId, payload.DisplayName, payload.PictureUrl, cancellationToken)
			.ConfigureAwait(false);

		var sessionToken = await sessions
			.CreateSessionAsync(payload.UserId, cancellationToken)
			.ConfigureAwait(false);

		AppendSessionCookie(context, sessionToken);

		return Results.NoContent();
	}

	// 前端載入時要知道「現在算不算已登入」，同時是唯一能重新簽 cookie 的地方。
	private static async Task<IResult> GetSessionAsync(
		HttpContext context,
		ISessionStore sessions,
		CancellationToken cancellationToken)
	{
		var sessionToken = context.Request.Cookies[SessionCookie.Name];

		if (string.IsNullOrEmpty(sessionToken))
			return Results.Unauthorized();

		var userId = await sessions.ResolveUserIdAsync(sessionToken, cancellationToken).ConfigureAwait(false);

		if (userId is null)
			return Results.Unauthorized();

		await sessions.RefreshAsync(sessionToken, cancellationToken).ConfigureAwait(false);
		AppendSessionCookie(context, sessionToken);

		return Results.Ok(new SessionResponse(userId));
	}

	private static async Task<IResult> LogoutAsync(
		HttpContext context,
		ISessionStore sessions,
		IConnectionAuthenticator authenticator,
		CancellationToken cancellationToken)
	{
		var sessionToken = context.Request.Cookies[SessionCookie.Name];

		if (!string.IsNullOrEmpty(sessionToken))
		{
			// 順序不能反：撤銷之後就查不到這個 token 屬於誰，也就沒辦法把他的連線踢掉。
			var userId = await sessions.ResolveUserIdAsync(sessionToken, cancellationToken).ConfigureAwait(false);

			await sessions.RevokeAsync(sessionToken, cancellationToken).ConfigureAwait(false);

			// 少了這一步，「登出」之後那條 WebSocket 仍然有效直到 client 自己關閉。
			if (userId is not null)
				await authenticator.TerminateConnectionsAsync(userId, cancellationToken).ConfigureAwait(false);
		}

		// 不論原本有沒有有效的 session 都清 cookie 並回同樣的結果：呼叫端無法從回應分辨
		// 手上那個 token 是否有效。
		context.Response.Cookies.Delete(SessionCookie.Name, CookieOptions(includeLifetime: false));

		return Results.NoContent();
	}

	private static void AppendSessionCookie(HttpContext context, string sessionToken) =>
		context.Response.Cookies.Append(SessionCookie.Name, sessionToken, CookieOptions(includeLifetime: true));

	// Delete 要能真的刪掉，屬性必須跟 Append 時一致（差一個 Path 就刪不掉）。
	private static CookieOptions CookieOptions(bool includeLifetime) => new()
	{
		// XSS 讀不到（ADR-2 的前提）
		HttpOnly = true,
		// http://localhost 被瀏覽器視為 trustworthy origin，所以本機開發也送得出去
		Secure = true,
		// 攻擊者頁面發起的 cross-site 請求不會帶上它——這是 CSWSH 的第一層防護
		SameSite = SameSiteMode.Lax,
		Path = "/",
		MaxAge = includeLifetime ? _CookieLifetime : null,
	};

	private sealed record LoginRequest(string IdToken, string Nonce);

	private sealed record NonceResponse(string Nonce);

	private sealed record SessionResponse(string UserId);
}
