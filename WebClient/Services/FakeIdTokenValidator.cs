namespace WebClient.Services;

// 開發用的假驗證器：把 idToken 字串直接當成 userId，不做任何驗證。
//
// 存在的理由是它讓端到端驗證（登入 → WebSocket → 開第二個分頁確認 Supersede）在拿到 Google
// OAuth client id 之前就能做。**這是一個完整的身分偽造後門**，所以有兩道鎖：
// 只在 Development 環境、而且 Login:AllowFakeIdTokens 明確設成 true 時才註冊（見 Program.cs），
// 並且啟動時會印一行 Warning。
internal sealed class FakeIdTokenValidator : IIdTokenValidator
{
	public bool IsConfigured => true;

	public ValueTask<IdTokenPayload?> ValidateAsync(
		string idToken,
		string expectedNonce,
		CancellationToken cancellationToken = default) =>
		new(new IdTokenPayload(idToken, $"Dev {idToken}", null));
}
