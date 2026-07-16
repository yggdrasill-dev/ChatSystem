namespace Common.Connections;

// ConnectionId -> NodeId 的跨節點對照表。由連線層自己擁有，不透過任何業務服務。
// 見 docs/architecture/connection-layer.md 第 6.2 節。
public interface IConnectionDirectory
{
	ValueTask RegisterAsync(string connectionId, string nodeId, CancellationToken cancellationToken = default);

	ValueTask UnregisterAsync(string connectionId, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyDictionary<string, string>> ResolveNodesAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default);
}
