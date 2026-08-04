using StackExchange.Redis;

namespace Common.Identity;

internal sealed class RedisPresenceDirectory(IConnectionMultiplexer multiplexer) : IPresenceDirectory
{
	// 比照 RedisConnectionDirectory 的保守暫定值，同樣未經負載測試。
	private const int MaxConcurrentLookups = 64;

	// 只有目前值等於 ARGV[1] 時才刪除。
	//
	// 為什麼需要這個比對：Supersede 會讓同一個使用者在極短時間內換連線，新連線先綁定、
	// 舊連線的解綁後到。無條件刪除的話，一個明明在線的使用者會從 Presence 消失，房間
	// fan-out 就靜默跳過他——殘留舊值是無害的（下次綁定會覆寫），誤刪是丟訊息。
	//
	// 刻意只碰單一 key：Presence 的 key 沒有 hash tag，跨 slot 的多 key script 在
	// Redis Cluster 上會被拒絕，所以「順手把別的 key 一起處理掉」在這裡不是選項。
	private const string CompareAndDeleteScript =
		"if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0";

	public async ValueTask<string?> BindAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		// SET ... GET 一次原子完成「取回舊值 + 寫入新值」。拆成 GET 再 SET 的話，兩條連線
		// 同時綁同一身分時可能都以為自己沒有前任，留下一條還活著、但 Presence 已經不指向
		// 它的孤兒連線——那條連線收不到任何 fan-out，症狀是「有人在房間裡卻看不到訊息」。
		var previous = await multiplexer.GetDatabase()
			.StringSetAndGetAsync(IdentityKeys.Presence(userId), connectionId, null, false, When.Always)
			.ConfigureAwait(false);

		return previous.IsNullOrEmpty ? null : (string?)previous;
	}

	public ValueTask UnbindAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().ScriptEvaluateAsync(
			CompareAndDeleteScript,
			[IdentityKeys.Presence(userId)],
			[connectionId]));

	public async ValueTask<IReadOnlyCollection<string>> ResolveConnectionsAsync(
		IReadOnlyCollection<string> userIds,
		CancellationToken cancellationToken = default)
	{
		// 跟 ResolveNodesAsync 同樣的處境：key 分散在不同 slot、無法用單一 MGET，
		// 所以平行送出多個 GET 但限制同時進行的數量。
		var ids = userIds.ToArray();
		var database = multiplexer.GetDatabase();
		var values = new RedisValue[ids.Length];

		await Parallel.ForEachAsync(
			Enumerable.Range(0, ids.Length),
			new ParallelOptions
			{
				MaxDegreeOfParallelism = MaxConcurrentLookups,
				CancellationToken = cancellationToken
			},
			async (i, _) => values[i] = await database
				.StringGetAsync(IdentityKeys.Presence(ids[i]))
				.ConfigureAwait(false)
		).ConfigureAwait(false);

		// 查不到的 userId（不在線）直接省略，不報錯。
		return [.. values.Where(value => !value.IsNullOrEmpty).Select(value => (string)value!)];
	}
}
