using StackExchange.Redis;

namespace Common.Rooms;

internal sealed class RedisRoomStore(IConnectionMultiplexer multiplexer) : IRoomStore
{
	// 比照 RedisConnectionDirectory 的保守暫定值，同樣未經負載測試。
	private const int MaxConcurrentLookups = 64;

	public async ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default)
	{
		var entries = await multiplexer.GetDatabase().HashGetAllAsync(RoomKeys.Room(roomId)).ConfigureAwait(false);

		return ToRoom(roomId, entries);
	}

	public async ValueTask<IReadOnlyCollection<Room>> ListOpenAsync(CancellationToken cancellationToken = default)
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

		// null 的來源是「index 裡有 id 但 hash 不存在」——TryCreateAsync 中途失敗留下的殘留，
		// 一律當成房間不存在跳過。
		return [.. rooms.OfType<Room>().Where(room => !room.IsClosed)];
	}

	public async ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();

		// SADD 的回傳值就是原子守門：只有真正把 id 加進 index 的那一次才繼續寫 hash。
		// 這是本介面唯一不能有競爭的操作。
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
		var existing = await GetAsync(roomId, cancellationToken).ConfigureAwait(false);

		if (existing is null || existing.IsClosed)
			return false;

		// 與 TryCloseAsync 併發時可能改到一個正在被關閉的房間的設定。刻意接受：已關閉的
		// 房間，設定沒有意義（見 room-layer.md 6.1）。
		await multiplexer.GetDatabase().HashSetAsync(
			RoomKeys.Room(roomId),
			[
				new HashEntry(RoomKeys.Fields.Name, name),
				new HashEntry(RoomKeys.Fields.PasswordHash, passwordHash ?? string.Empty),
			]).ConfigureAwait(false);

		return true;
	}

	public async ValueTask<bool> TryCloseAsync(string roomId, CancellationToken cancellationToken = default)
	{
		var database = multiplexer.GetDatabase();
		var isClosed = await database.HashGetAsync(RoomKeys.Room(roomId), RoomKeys.Fields.IsClosed).ConfigureAwait(false);

		// 房間不存在（欄位讀不到）或已經關閉，都回 false 讓呼叫端知道沒有實際生效。
		if (isClosed.IsNull || isClosed == "1")
			return false;

		// 兩個 close 併發時兩邊都可能走到這裡、都回 true，導致 RoomClosed 廣播兩次。
		// 刻意接受：client 對重複的關閉通知照 idempotent 處理。
		await database.HashSetAsync(RoomKeys.Room(roomId), RoomKeys.Fields.IsClosed, "1").ConfigureAwait(false);

		return true;
	}

	private static HashEntry[] ToEntries(Room room) =>
	[
		new(RoomKeys.Fields.Name, room.Name),
		new(RoomKeys.Fields.PasswordHash, room.PasswordHash ?? string.Empty),
		new(RoomKeys.Fields.OwnerUserId, room.OwnerUserId),
		new(RoomKeys.Fields.CreatedAt, room.CreatedAt.ToUnixTimeMilliseconds()),
		new(RoomKeys.Fields.IsClosed, room.IsClosed ? "1" : "0"),
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
			DateTimeOffset.FromUnixTimeMilliseconds((long?)Field(fields, RoomKeys.Fields.CreatedAt) ?? 0),
			Field(fields, RoomKeys.Fields.IsClosed) == "1");
	}

	private static RedisValue Field(Dictionary<string, RedisValue> fields, string name) =>
		fields.TryGetValue(name, out var value) ? value : RedisValue.Null;
}
