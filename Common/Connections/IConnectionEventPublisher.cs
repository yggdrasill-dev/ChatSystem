namespace Common.Connections;

// 連線層對上層發出的事件。目前只有一個事件——連線已關閉。
//
// 這是連線層唯一「主動往上告知」的通道：其他對外能力（IOutboundGateway、
// IConnectionTerminator）都是上層呼叫下來，這個是反過來的。
//
// 語意是 best-effort：process 被 kill 時事件發不出來，publish 本身也可能失敗，
// 所以訂閱端必須設計成「沒收到事件也會正確」（見 connection-layer.md ADR-8）。
public interface IConnectionEventPublisher
{
	// principal 是 handshake 驗證通過的不透明字串（連線層轉手、不解讀）。帶上它，訂閱端
	// 就不需要為了知道「是誰斷線了」而維護 connectionId -> 身分的反向索引。
	ValueTask PublishDisconnectedAsync(
		string connectionId,
		string nodeId,
		string principal,
		CancellationToken cancellationToken = default);
}
