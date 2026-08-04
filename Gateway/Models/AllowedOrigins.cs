namespace Gateway.Models;

// handshake 的 Origin allowlist。
//
// WebSocket handshake **不受 CORS 約束**：任何網站都能對 Gateway 開一條 WebSocket，而瀏覽器
// 會照常帶上受害者的 cookie（Cross-Site WebSocket Hijacking）。既然身分驗證靠 cookie，這個
// 檢查就不是可選的加強項。SameSite=Lax 也擋得住，但那個防護的前提是 cookie 真的維持 Lax，
// 跨站部署時會被迫降成 None——兩層都做，成本只是一個字串比對。
//
// 沒有 Origin 的請求一律拒絕：現階段唯一的 client 是瀏覽器，而瀏覽器一定會帶。未來要支援
// 原生 client 時，那類 client 也不會用 cookie 認證，需要另一條驗證路徑。
public sealed class AllowedOrigins(IEnumerable<string> origins)
{
	// host 本身大小寫不敏感，比對也就不該敏感。
	private readonly HashSet<string> m_Origins = new(origins, StringComparer.OrdinalIgnoreCase);

	public bool IsAllowed(string? origin) => !string.IsNullOrEmpty(origin) && m_Origins.Contains(origin);
}
