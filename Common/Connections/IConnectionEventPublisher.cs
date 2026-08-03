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
	ValueTask PublishDisconnectedAsync(
		string connectionId,
		string nodeId,
		CancellationToken cancellationToken = default);
}
