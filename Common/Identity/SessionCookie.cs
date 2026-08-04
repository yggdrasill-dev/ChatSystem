namespace Common.Identity;

// Identity 服務簽發、Gateway 在 handshake 讀，兩邊必須是同一個名字，所以放在共用組件而不是
// 各自寫死一份字面值。
//
// cookie 屬性（httpOnly / Secure / SameSite=Lax）由簽發端決定，不在這裡——讀取端不需要
// 知道那些，而且 Gateway 對 cookie 的唯一動作就是把值原封不動交給身分層。
public static class SessionCookie
{
	public const string Name = "chat_session";
}
