using Google.Protobuf;

namespace Common.Delivery;

// 上層呼叫連線層的唯一入口。參數只接受 ConnectionId，不接受任何業務身分。
// 見 docs/architecture/connection-layer.md 第 6.3 節、ADR-3。
public interface IOutboundGateway
{
	ValueTask DeliverAsync(
		string subject,
		IReadOnlyCollection<string> connectionIds,
		ByteString payload,
		CancellationToken cancellationToken = default);
}
