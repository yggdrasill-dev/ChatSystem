using StackExchange.Redis;

namespace Common.Rooms;

internal sealed class RedisRoomMembership(IConnectionMultiplexer multiplexer, TimeProvider timeProvider) : IRoomMembership
{
	// 斷線後成員仍留在名單上的時間。憑經驗抓的暫定值，未經負載測試（room-layer.md ADR-2）。
	internal static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

	// 成員的 value 打包成一個字串而不是每個成員一個 Hash：fan-out 要的是「整間房的名單」，
	// 一次 HGETALL 就拿完。connectionId 是 32 位 hex，不會出現分隔符。
	private const char Separator = '|';

	// userId → roomId 的指向只在還指向這個房間時才刪。使用者可能已經加入別的房間，
	// 那時舊房間的 RemoveAsync 不能把新的指向蓋掉——跟身分層解綁 Presence 是同一個
	// fencing 問題（identity-layer.md ADR-4）。
	private const string ReleaseUserRoomScript =
		"if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) end return 0";

	// 標記斷線 = 檢查 fencing + 寫入 DisconnectedAt + 排進 sweeper 的待辦清單。三件事必須
	// 一起發生：只標記不排程的話 sweeper 永遠不知道，只排程不標記的話讀取時的過濾會漏掉。
	//
	// 能用一段 Lua 跨兩個 key 做完，正是 {rooms} hash tag 換來的（room-layer.md 6.1 當初
	// 就是為了「未來真的需要 Lua 時」才付那個代價的）。
	private const string MarkDisconnectedScript =
		"""
		local packed = redis.call('HGET', KEYS[1], ARGV[1])
		if not packed then return 0 end
		local joinedAt, connectionId = string.match(packed, '^([^|]*)|([^|]*)|')
		if connectionId ~= ARGV[2] then return 0 end
		redis.call('HSET', KEYS[1], ARGV[1], joinedAt .. '|' .. connectionId .. '|' .. ARGV[3])
		redis.call('ZADD', KEYS[2], ARGV[4], ARGV[5])
		return 1
		""";

	public async ValueTask<IReadOnlyCollection<RoomMember>> GetMembersAsync(
		string roomId,
		CancellationToken cancellationToken = default)
	{
		var entries = await multiplexer.GetDatabase()
			.HashGetAllAsync(RoomKeys.Members(roomId))
			.ConfigureAwait(false);

		var cutoff = timeProvider.GetUtcNow() - GracePeriod;

		// 讀取時就把寬限期已過的濾掉。這是 ADR-2 正確性的來源——不依賴 sweeper 有沒有跑，
		// 也不依賴它跑得多快。
		return
		[
			.. entries
				.Select(entry => Unpack((string)entry.Name!, entry.Value))
				.OfType<RoomMember>()
				.Where(member => member.DisconnectedAt is null || member.DisconnectedAt > cutoff)
		];
	}

	public async ValueTask<string?> GetCurrentRoomAsync(string userId, CancellationToken cancellationToken = default)
	{
		var roomId = await multiplexer.GetDatabase()
			.StringGetAsync(RoomKeys.UserRoom(userId))
			.ConfigureAwait(false);

		return roomId.IsNullOrEmpty ? null : (string?)roomId;
	}

	public async ValueTask JoinAsync(
		string roomId,
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();

		// 重連時會覆寫既有的成員記錄，JoinedAt 因此被重設。無害：JoinedAt 不會廣播給其他
		// 成員，而寬限期重連的重點正是「其他成員什麼都看不到」。
		await database
			.HashSetAsync(RoomKeys.Members(roomId), userId, Pack(timeProvider.GetUtcNow(), connectionId, null))
			.ConfigureAwait(false);

		await database.StringSetAsync(RoomKeys.UserRoom(userId), roomId).ConfigureAwait(false);

		// 清掉可能存在的寬限期標記：這個人回來了，sweeper 不該再處理他。
		await database.SortedSetRemoveAsync(RoomKeys.Grace, GraceEntry(roomId, userId)).ConfigureAwait(false);
	}

	public async ValueTask RemoveAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();

		await database.HashDeleteAsync(RoomKeys.Members(roomId), userId).ConfigureAwait(false);
		await database.SortedSetRemoveAsync(RoomKeys.Grace, GraceEntry(roomId, userId)).ConfigureAwait(false);

		await database
			.ScriptEvaluateAsync(ReleaseUserRoomScript, [RoomKeys.UserRoom(userId)], [roomId])
			.ConfigureAwait(false);
	}

	public async ValueTask MarkDisconnectedAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		// 先問「這個人在哪間房」。這一步跟下面的 Lua 之間如果使用者加入了別的房間，Lua 會
		// 對舊房間的名單操作——而他已經被 RemoveAsync 移出舊房間，HGET 拿不到，變成 no-op。
		var roomId = await GetCurrentRoomAsync(userId, cancellationToken).ConfigureAwait(false);

		if (roomId is null)
			return;

		var now = timeProvider.GetUtcNow();

		await multiplexer.GetDatabase()
			.ScriptEvaluateAsync(
				MarkDisconnectedScript,
				[RoomKeys.Members(roomId), RoomKeys.Grace],
				[
					userId,
					connectionId,
					now.ToUnixTimeMilliseconds(),
					(now + GracePeriod).ToUnixTimeMilliseconds(),
					GraceEntry(roomId, userId),
				])
			.ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyCollection<(string RoomId, string UserId)>> ListExpiredAsync(
		CancellationToken cancellationToken = default)
	{
		var expired = await multiplexer.GetDatabase()
			.SortedSetRangeByScoreAsync(
				RoomKeys.Grace,
				double.NegativeInfinity,
				timeProvider.GetUtcNow().ToUnixTimeMilliseconds())
			.ConfigureAwait(false);

		return [.. expired.Select(entry => ParseGraceEntry((string)entry!)).OfType<(string, string)>()];
	}

	private static RedisValue Pack(DateTimeOffset joinedAt, string connectionId, DateTimeOffset? disconnectedAt) =>
		string.Join(
			Separator,
			joinedAt.ToUnixTimeMilliseconds(),
			connectionId,
			disconnectedAt?.ToUnixTimeMilliseconds().ToString() ?? string.Empty);

	private static RoomMember? Unpack(string userId, RedisValue packed)
	{
		if (packed.IsNullOrEmpty)
			return null;

		var parts = ((string)packed!).Split(Separator);

		if (parts.Length < 3 || !long.TryParse(parts[0], out var joinedAt))
			return null;

		return new RoomMember(
			userId,
			DateTimeOffset.FromUnixTimeMilliseconds(joinedAt),
			parts[1],
			long.TryParse(parts[2], out var disconnectedAt)
				? DateTimeOffset.FromUnixTimeMilliseconds(disconnectedAt)
				: null);
	}

	private static RedisValue GraceEntry(string roomId, string userId) => $"{roomId}{Separator}{userId}";

	private static (string RoomId, string UserId)? ParseGraceEntry(string entry)
	{
		var parts = entry.Split(Separator);

		return parts.Length == 2 ? (parts[0], parts[1]) : null;
	}
}
