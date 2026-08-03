namespace Common.Connections;

// 上層主動終止指定連線的入口。跟 IOutboundGateway 一樣只接受 ConnectionId，不接受任何業務身分；
// 為什麼是獨立介面而不併入 IOutboundGateway，見 connection-layer.md ADR-7。
public interface IConnectionTerminator
{
	ValueTask TerminateAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default);
}
