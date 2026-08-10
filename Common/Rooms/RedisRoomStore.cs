using StackExchange.Redis;

namespace Common.Rooms;

internal sealed class RedisRoomStore(IConnectionMultiplexer multiplexer) : IRoomStore
{
	// 比照 RedisConnectionDirectory 的保守暫定值，同樣未經負載測試。
	private const int MaxConcurrentLookups = 64;

	// 「房間還在才改設定」。ADR-10 之前這個守門是 existing.IsClosed，真刪之後沒有那個欄位可以
	// 看了，而 HSET 對不存在的 key 會**建立**它——update 與 delete 併發就會留下一間只有 name 與
	// password_hash、不在 index 上、卻 GetAsync 得到的殭屍房。Postgres 版不需要這段（UPDATE
	// 影響 0 列自然回 false），所以這是 Redis 版獨有的一次性成本。
	//
	// 順帶把原本的 GetAsync + HSET 收成一次往返：呼叫端（RoomUpdateHandler）本來就自己讀過
	// 房間做房主檢查，這裡再讀一次只是為了守門。
	private const string UpdateSettingsScript =
		"""
		if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
		redis.call('HSET', KEYS[1], ARGV[1], ARGV[2], ARGV[3], ARGV[4])
		return 1
		""";

	public async ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default)
	{
		var entries = await multiplexer.GetDatabase().HashGetAllAsync(RoomKeys.Room(roomId)).ConfigureAwait(false);

		return ToRoom(roomId, entries);
	}

	public async ValueTask<IReadOnlyCollection<Room>> ListAsync(CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();
		var roomIds = await database.SetMembersAsync(RoomKeys.Index).ConfigureAwait(false);
		var rooms = new Room?[roomIds.Length];

		// N+1：SMEMBERS 之後逐筆 HGETALL。房間數量小的時候沒問題，這是 room-layer.md §8
		// 把分頁列為「先不做」時心裡有數的成本。併發上限比照 RedisConnectionDirectory。
		await Parallel.ForEachAsync(
			Enumerable.Range(0, roomIds.Length),
			new ParallelOptions
			{
				MaxDegreeOfParallelism = MaxConcurrentLookups,
				CancellationToken = cancellationToken
			},
			async (i, _) =>
			{
				var roomId = (string)roomIds[i]!;
				rooms[i] = ToRoom(roomId, await database.HashGetAllAsync(RoomKeys.Room(roomId)).ConfigureAwait(false));
			}).ConfigureAwait(false);

		// null 的來源是「index 裡有 id 但 hash 不存在」——TryCreateAsync 或 TryDeleteAsync 中途
		// 失敗留下的殘留，一律當成房間不存在跳過。ADR-10 之後這個過濾更重要了：真刪有兩步，
		// 而先前只有建立那一步會留殘留。
		return [.. rooms.OfType<Room>()];
	}

	public async ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();

		// SADD 的回傳值就是原子守門：只有真正把 id 加進 index 的那一次才繼續寫 hash。
		if (!await database.SetAddAsync(RoomKeys.Index, room.RoomId).ConfigureAwait(false))
			return false;

		await database.HashSetAsync(RoomKeys.Room(room.RoomId), ToEntries(room)).ConfigureAwait(false);

		return true;
	}

	public async ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default)
	{
		var applied = await multiplexer.GetDatabase()
			.ScriptEvaluateAsync(
				UpdateSettingsScript,
				[RoomKeys.Room(roomId)],
				[
					RoomKeys.Fields.Name,
					name,
					RoomKeys.Fields.PasswordHash,
					passwordHash ?? string.Empty,
				])
			.ConfigureAwait(false);

		return (long)applied == 1;
	}

	public async ValueTask<bool> TryDeleteAsync(string roomId, CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();

		// 刪 hash 是原子守門，對稱於 TryCreateAsync 的 SADD：只有真的刪掉的那一次回 true。
		// 兩個 close 併發因此只有一個會廣播 RoomClosed——6.1 原本刻意接受的重複廣播消失了。
		if (!await database.KeyDeleteAsync(RoomKeys.Room(roomId)).ConfigureAwait(false))
			return false;

		// 順序刻意是「先 hash 後 index」：中途死掉留下的是「index 有 id、hash 不存在」，那正是
		// ListAsync 已經在濾的殘留形狀。反過來會留下一間列不出來卻查得到的房間。
		await database.SetRemoveAsync(RoomKeys.Index, roomId).ConfigureAwait(false);

		// Redis 沒有 ON DELETE CASCADE，封鎖名單只能自己帶走（ADR-10 的 CASCADE 是 Postgres
		// 版才有的東西）。成員名單不在這裡清：RoomCloseHandler 得先讀名單才知道要通知誰，
		// 所以清理跟著廣播走。
		await database.KeyDeleteAsync(RoomKeys.Ban(roomId)).ConfigureAwait(false);

		return true;
	}

	private static HashEntry[] ToEntries(Room room) =>
	[
		new(RoomKeys.Fields.Name, room.Name),
		new(RoomKeys.Fields.PasswordHash, room.PasswordHash ?? string.Empty),
		new(RoomKeys.Fields.OwnerUserId, room.OwnerUserId),
		new(RoomKeys.Fields.CreatedAt, room.CreatedAt.ToUnixTimeMilliseconds()),
	];

	private static Room? ToRoom(string roomId, HashEntry[] entries)
	{
		if (entries.Length == 0)
			return null;

		var fields = entries.ToDictionary(entry => (string)entry.Name!, entry => entry.Value);

		var passwordHash = Field(fields, RoomKeys.Fields.PasswordHash);

		return new Room(
			roomId,
			(string?)Field(fields, RoomKeys.Fields.Name) ?? string.Empty,
			passwordHash.IsNullOrEmpty ? null : (string?)passwordHash,
			(string?)Field(fields, RoomKeys.Fields.OwnerUserId) ?? string.Empty,
			DateTimeOffset.FromUnixTimeMilliseconds((long?)Field(fields, RoomKeys.Fields.CreatedAt) ?? 0));
	}

	private static RedisValue Field(Dictionary<string, RedisValue> fields, string name) =>
		fields.TryGetValue(name, out var value) ? value : RedisValue.Null;
}
