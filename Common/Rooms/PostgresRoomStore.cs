using Dapper;
using Npgsql;

namespace Common.Rooms;

// chat-layer.md ADR-4 的正式儲存。**每個方法都是單一語句**，沒有 read-modify-write、不需要交易
// ——那是 IRoomStore「一次到位、回傳有沒有生效」那條介面約定換來的。
//
// 欄位在 SQL 裡直接 alias 成 C# 的名字，而不是打開 Dapper 的 `MatchNamesWithUnderscores`：
// 那是個全域 static，會影響整個 process 裡所有 Dapper 對映。
internal sealed class PostgresRoomStore(NpgsqlDataSource dataSource) : IRoomStore
{
	// **不能直接把 Room 交給 Dapper 對映。** Npgsql 把 `timestamptz` 讀成 `DateTime`（Kind=Utc），
	// 而 `Room.CreatedAt` 是 `DateTimeOffset`；Dapper 的建構子對映要求型別對得上，對不上就丟
	// 「A parameterless default constructor or one matching signature ... is required」。
	//
	// 中間放一個 row 型別明確轉換，而不是註冊 Dapper 的 TypeHandler——後者是全域 static，會影響
	// 整個 process 裡所有的 Dapper 對映。寫入方向不需要這一層：Npgsql 收 DateTimeOffset 沒問題。
	private sealed record RoomRow(
		string RoomId,
		string Name,
		string? PasswordHash,
		string OwnerUserId,
		DateTime CreatedAt)
	{
		public Room ToRoom() =>
			new(
				RoomId,
				Name,
				PasswordHash,
				OwnerUserId,
				new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc), TimeSpan.Zero));
	}

	private const string Columns =
		"room_id AS RoomId, name AS Name, password_hash AS PasswordHash, " +
		"owner_user_id AS OwnerUserId, created_at AS CreatedAt";

	public async ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		var row = await connection
			.QuerySingleOrDefaultAsync<RoomRow>(new CommandDefinition(
				$"SELECT {Columns} FROM rooms WHERE room_id = @roomId",
				new { roomId },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		return row?.ToRoom();
	}

	public async ValueTask<IReadOnlyCollection<Room>> ListAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// 沒有 ORDER BY：順序不在 IRoomStore 的契約裡（RoomStoreContractTests 明確不斷言它）。
		// 房間數量的成長由 room-layer.md §8 那條「分頁先不做」承接，不在這裡處理。
		var rows = await connection
			.QueryAsync<RoomRow>(new CommandDefinition(
				$"SELECT {Columns} FROM rooms",
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		return [.. rows.Select(row => row.ToRoom())];
	}

	public async ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// **`DO NOTHING` 而不是 `DO UPDATE`**：撞到既有 id 時不能覆寫，那是本介面唯一不能有競爭
		// 的操作。回 false 跟「原本那間房沒被動到」是兩件事，契約測試兩件都斷言。
		var inserted = await connection
			.ExecuteAsync(new CommandDefinition(
				"""
				INSERT INTO rooms (room_id, name, password_hash, owner_user_id, created_at)
				VALUES (@RoomId, @Name, @PasswordHash, @OwnerUserId, @CreatedAt)
				ON CONFLICT (room_id) DO NOTHING
				""",
				room,
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		return inserted == 1;
	}

	public async ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// **只動 name 與 password_hash**：房主與建立時間不可變更（Room 的註解說明後台三個流程
		// 都靠「房主永遠不會變」才對 TOCTOU 安全）。整列 UPDATE 會靜默拆掉那個前提。
		//
		// 房間不存在就是影響 0 列、回 false——**Redis 版需要一段 EXISTS 守門的 Lua 才做得到
		// 同一件事**（HSET 會建立不存在的 key），這裡是 UPDATE 的天然語意。
		var updated = await connection
			.ExecuteAsync(new CommandDefinition(
				"UPDATE rooms SET name = @name, password_hash = @passwordHash WHERE room_id = @roomId",
				new { roomId, name, passwordHash },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		return updated == 1;
	}

	public async ValueTask<bool> TryDeleteAsync(string roomId, CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// 封鎖名單（以及階段 B 下一步的 messages）靠 `ON DELETE CASCADE` 跟著走，這裡不必自己刪
		// ——**Redis 版那行手動刪 key 的程式碼到這裡就消失了**（ADR-10）。
		//
		// 影響列數就是守門：兩個併發的 close 只有一個刪得到，所以 RoomClosed 不會廣播兩次。
		var deleted = await connection
			.ExecuteAsync(new CommandDefinition(
				"DELETE FROM rooms WHERE room_id = @roomId",
				new { roomId },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		return deleted == 1;
	}
}
