using StackExchange.Redis;

namespace Common.Connections;

internal sealed class RedisConnectionDirectory(IConnectionMultiplexer multiplexer) : IConnectionDirectory
{
	private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

	public ValueTask RegisterAsync(string connectionId, string nodeId, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().StringSetAsync(Key(connectionId), nodeId, Ttl));

	public ValueTask UnregisterAsync(string connectionId, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().KeyDeleteAsync(Key(connectionId)));

	public async ValueTask<IReadOnlyDictionary<string, string>> ResolveNodesAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default)
	{
		// 真實 Redis Cluster：不同 connectionId 的 key 分散在不同 slot，
		// 無法用單一 MGET 跨 slot 查詢；改成平行送出多個 GET、一次 await 全部完成。
		var ids = connectionIds.ToArray();
		var database = multiplexer.GetDatabase();
		var values = await Task.WhenAll(ids.Select(id => database.StringGetAsync(Key(id)))).ConfigureAwait(false);

		var result = new Dictionary<string, string>();

		for (var i = 0; i < ids.Length; i++)
			if (!values[i].IsNullOrEmpty)
				result[ids[i]] = values[i]!;

		return result; // 查不到的 connectionId（已斷線/過期）直接省略，不報錯
	}

	private static string Key(string connectionId) => $"Conn:{connectionId}";
}
