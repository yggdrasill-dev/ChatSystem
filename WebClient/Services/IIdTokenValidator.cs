namespace WebClient.Services;

// 驗證過的 ID Token 內容。只取這個系統真的會用到的三個 claim。
public sealed record IdTokenPayload(string UserId, string? DisplayName, string? PictureUrl);

// 把 Google ID Token 的驗證包成介面的唯一理由是可測試性：
// GoogleJsonWebSignature.ValidateAsync 是 static 方法，不包起來就沒有辦法在測試裡替換。
//
// 這個介面刻意留在 WebClient 專案裡、不進 Common——Google.Apis.Auth 只有這裡需要，
// 放 Common 會讓 Gateway 與 CommandRouter 一起把它拖進去。
public interface IIdTokenValidator
{
	// false = 這個部署沒有設定 Google client id，登入不可能成功（endpoint 直接回 503）。
	bool IsConfigured { get; }

	// 驗證失敗（簽章、aud、iss、exp、nonce 任一項不符）一律回 null，不區分原因：
    // 對呼叫端來說能做的事都一樣，而區分原因等於告訴攻擊者他錯在哪。
	ValueTask<IdTokenPayload?> ValidateAsync(
		string idToken,
		string expectedNonce,
		CancellationToken cancellationToken = default);
}
