namespace Common.Identity;

// 登入狀態：SessionToken -> UserId。
//
// 存活期跟 WebSocket 連線的存活期完全無關（identity-layer.md ADR-3）——斷線重連、換分頁、
// 換網路都不需要重新走一次 Google 登入。
public interface ISessionStore
{
	ValueTask<string> CreateSessionAsync(string userId, CancellationToken cancellationToken = default);

	// 找不到或已過期回 null。
	ValueTask<string?> ResolveUserIdAsync(string sessionToken, CancellationToken cancellationToken = default);

	// sliding 續期。呼叫時機是 handshake（連線建立是低頻事件，多一次寫入無所謂）；
	// 刻意不做成「每則命令都續期」，那會變成每則訊息一次 Redis 寫入。
	ValueTask RefreshAsync(string sessionToken, CancellationToken cancellationToken = default);

	// 登出。
	ValueTask RevokeAsync(string sessionToken, CancellationToken cancellationToken = default);
}
