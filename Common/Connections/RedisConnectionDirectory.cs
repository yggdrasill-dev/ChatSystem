using StackExchange.Redis;

namespace Common.Connections;

internal sealed class RedisConnectionDirectory(IConnectionMultiplexer multiplexer) : IConnectionDirectory
{
	private static readonly TimeSpan _Ttl = TimeSpan.FromSeconds(30);

	// 大批次投遞（上萬個 connectionId）時避免一次開出上萬條平行 Redis GET。
	// 64 目前是憑經驗抓的保守暫定值，沒有經過實際負載測試驗證；等有生產環境的實測數據（Redis 拓樸、延遲、批次量級）再調整。
	private const int MaxConcurrentLookups = 64;

	public ValueTask RegisterAsync(string connectionId, string nodeId, CancellationToken cancellationToken = default)
		=> new(multiplexer.GetDatabase().StringSetAsync(Key(connectionId), nodeId, _Ttl));

	public ValueTask UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
		=> new(multiplexer.GetDatabase().KeyDeleteAsync(Key(connectionId)));

	public async ValueTask<IReadOnlyDictionary<string, string>> ResolveNodesAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default)
	{
		// 真實 Redis Cluster：不同 connectionId 的 key 分散在不同 slot，
		// 無法用單一 MGET 跨 slot 查詢；改成平行送出多個 GET，但用 MaxDegreeOfParallelism 限制同時進行的數量。
		var ids = connectionIds.ToArray();
		var database = multiplexer.GetDatabase();
		var values = new RedisValue[ids.Length];

		await Parallel.ForEachAsync(
			Enumerable.Range(0, ids.Length),
			new ParallelOptions
			{
				MaxDegreeOfParallelism = MaxConcurrentLookups,
				CancellationToken = cancellationToken
			},
			async (i, ct) => values[i] = await database.StringGetAsync(Key(ids[i])).ConfigureAwait(false)
		).ConfigureAwait(false);

		var result = new Dictionary<string, string>();

		for (var i = 0; i < ids.Length; i++)
			if (!values[i].IsNullOrEmpty)
				result[ids[i]] = values[i]!;

		return result; // 查不到的 connectionId（已斷線/過期）直接省略，不報錯
	}

	private static string Key(string connectionId)
		=> $"Conn:{connectionId}";
}