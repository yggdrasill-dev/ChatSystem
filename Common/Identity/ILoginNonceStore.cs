namespace Common.Identity;

// Google 登入用的一次性 nonce（identity-layer.md ADR-5）。
//
// 防的是「攻擊者用自己合法取得的 ID Token，誘騙受害者瀏覽器送去登入 endpoint」——
// 即使 aud/iss/exp 全部驗證通過，沒有 nonce 就攔不住這種替換。
public interface ILoginNonceStore
{
	ValueTask<string> IssueAsync(CancellationToken cancellationToken = default);

	// 消耗成功回 true。必須是一次性的：只檢查「存在」而不消耗，等於同一個 nonce 可以重放。
	ValueTask<bool> TryConsumeAsync(string nonce, CancellationToken cancellationToken = default);
}
