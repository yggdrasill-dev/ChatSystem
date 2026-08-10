using Dapper;
using Npgsql;

namespace Common.Rooms;

// 複合主鍵 `(room_id, user_id)`，沒有 surrogate id：三個方法分別對應 SELECT / INSERT ...
// ON CONFLICT DO NOTHING / DELETE，全部靠主鍵，所以 room-layer.md 6.1 那句「IRoomBanList 因此
// 不需要 Try 語意」在這裡仍然成立。
//
// **這個實作比 Redis 版多一條前置條件：房間必須存在。** `room_id` 有 FK 指向 `rooms`（那是
// ADR-10 的 CASCADE 機制），所以對不存在的房間下封鎖會丟 23503。Redis 的 Set 從來不管這件事，
// 而那條前置條件一直都成立、只是沒被寫下來——`RoomBanHandler` 先讀房間、檢查房主才動作。
// 契約測試已經補上這條（B2 才發現）。
internal sealed class PostgresRoomBanList(NpgsqlDataSource dataSource) : IRoomBanList
{
	public async ValueTask<bool> IsBannedAsync(
		string roomId,
		string userId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		return await connection
			.ExecuteScalarAsync<bool>(new CommandDefinition(
				"SELECT EXISTS(SELECT 1 FROM room_bans WHERE room_id = @roomId AND user_id = @userId)",
				new { roomId, userId },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);
	}

	public async ValueTask BanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// `DO NOTHING` 就是介面要求的 idempotent：重複封鎖同一個人跟封鎖一次的結果一樣。
		// 少寫這一句換來的是 23505。
		await connection
			.ExecuteAsync(new CommandDefinition(
				"""
				INSERT INTO room_bans (room_id, user_id)
				VALUES (@roomId, @userId)
				ON CONFLICT (room_id, user_id) DO NOTHING
				""",
				new { roomId, userId },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);
	}

	public async ValueTask UnbanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		// 解鎖一個沒被封鎖的人是影響 0 列，不是錯誤——後台的「踢出＋封鎖」與「解鎖」是兩個
		// 獨立按鈕，重複點擊到得了這裡。
		await connection
			.ExecuteAsync(new CommandDefinition(
				"DELETE FROM room_bans WHERE room_id = @roomId AND user_id = @userId",
				new { roomId, userId },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);
	}
}
