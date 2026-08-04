namespace Common.Identity;

// 連線層在 handshake 與連線生命週期上呼叫的唯一身分層 hook。
//
// 把三件事（驗 Session、Supersede、解除綁定）包在這個介面後面，連線層因此只認識這一個
// 介面，不認識 ISessionStore / IPresenceDirectory，也不知道 Supersede 這條規則存在。
public interface IConnectionAuthenticator
{
	// handshake：驗證 SessionToken 並順便 sliding 續期。回傳 null = 拒絕這條連線。
	//
	// 參數刻意是 string? 而不是 HttpContext：Common 不該為了讀一個 cookie 取得 ASP.NET Core
	// 的 framework reference，而且這樣一來「怎麼拿到那個字串」完全是呼叫端的事。
	ValueTask<string?> ResolveAsync(string? sessionToken, CancellationToken cancellationToken = default);

	// 連線建立後：綁定 principal 到這條連線，並終止被取代掉的舊連線（Supersede）。
	//
	// **不要把這兩個方法改名成 BindAsync / UnbindAsync**。這個介面會被注入到 Gateway 的
	// minimal API endpoint，而 ASP.NET Core 把參數型別上任何叫 BindAsync 的成員當成自訂
	// 參數繫結慣例（必須是 static 且回傳 ValueTask<T>）。簽章不符的話，routing 階段就會丟
	// InvalidOperationException，症狀是每個請求都拿到 500——而且那個例外發生在 routing
	// middleware 裡，應用程式自己的 try/catch 攔不到，log 也只會看到 500。
	ValueTask BindConnectionAsync(string principal, string connectionId, CancellationToken cancellationToken = default);

	// 連線關閉後：解除綁定。只有目前綁的就是這條連線時才會生效（fencing）。
	ValueTask UnbindConnectionAsync(string principal, string connectionId, CancellationToken cancellationToken = default);

	// 登出：把這個身分目前的連線全部終止。
	//
	// 放在這個介面而不是讓呼叫端自己組（查 IPresenceDirectory 再呼叫 IConnectionTerminator），
	// 是因為 Supersede 已經在做同一件事——「這個身分的連線」這個概念屬於這裡。
	// 副作用是登出的 endpoint 只需要認識這一個介面。
	ValueTask TerminateConnectionsAsync(string principal, CancellationToken cancellationToken = default);
}
