namespace Common.Identity;

// 一個已登入身分目前繫結在哪一條連線上：UserId -> ConnectionId（單一值）。
//
// 之所以能是單一值，是「同一身分同時只有一條連線生效」（Supersede，identity-layer.md
// ADR-4）加上「一個使用者一次只能在一間房」兩個決定疊出來的。
//
// 這裡只有正向（身分 -> 連線）。反向（連線 -> 身分）刻意不存：那個問題的答案隨每則
// inbound 封包一起送上來（protocol-layer.md ADR-9），不需要查表。
public interface IPresenceDirectory
{
	// 綁定，並回傳「被這次綁定取代掉的舊 connectionId」（沒有則 null）。
	// 呼叫端拿它去終止舊連線；這個介面本身不知道「終止連線」這件事。
	ValueTask<string?> BindAsync(string userId, string connectionId, CancellationToken cancellationToken = default);

	// 解除綁定。只有當目前值等於 connectionId 時才生效（fencing，見實作）。
	ValueTask UnbindAsync(string userId, string connectionId, CancellationToken cancellationToken = default);

	// 房間 fan-out 用：一間房可能很多成員，逐筆查會變成 N 次來回。
	// 查不到的 userId（不在線）直接省略，沿用 ResolveNodesAsync 的既有慣例。
	ValueTask<IReadOnlyCollection<string>> ResolveConnectionsAsync(
		IReadOnlyCollection<string> userIds,
		CancellationToken cancellationToken = default);
}
