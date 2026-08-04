using Google.Apis.Auth;

namespace WebClient.Services;

internal sealed class GoogleIdTokenValidator(string? googleClientId) : IIdTokenValidator
{
	public bool IsConfigured => !string.IsNullOrEmpty(googleClientId);

	public async ValueTask<IdTokenPayload?> ValidateAsync(
		string idToken,
		string expectedNonce,
		CancellationToken cancellationToken = default)
	{
		if (!IsConfigured)
			return null;

		GoogleJsonWebSignature.Payload payload;

		try
		{
			// 簽章、iss、aud、exp 都由這裡驗（含向 Google 取金鑰並快取）。
			// ValidateAsync 沒有 CancellationToken 多載，所以這裡的 ct 只用於呼叫端的語意一致。
			payload = await GoogleJsonWebSignature
				.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
				{
					Audience = [googleClientId!]
				})
				.ConfigureAwait(false);
		}
		catch (InvalidJwtException)
		{
			return null;
		}

		// nonce 不在 Google 的驗證職責裡，必須自己比對。少了這一步，攻擊者可以拿自己合法
		// 取得的 ID Token 誘騙受害者的瀏覽器送來登入，受害者就被登入成攻擊者的帳號。
		if (!string.Equals(payload.Nonce, expectedNonce, StringComparison.Ordinal))
			return null;

		return new IdTokenPayload(payload.Subject, payload.Name, payload.Picture);
	}
}
