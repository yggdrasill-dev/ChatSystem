using Common.Storage;
using Microsoft.EntityFrameworkCore;

namespace Common.Rooms;

// 複合主鍵 `(room_id, user_id)`，沒有 surrogate id：三個方法分別對應 SELECT / INSERT ...
// ON CONFLICT DO NOTHING / DELETE，全部靠主鍵，所以 room-layer.md 6.1 那句「IRoomBanList 因此
// 不需要 Try 語意」在這裡仍然成立。
//
// **這個實作比 Redis 版多一條前置條件：房間必須存在。** `room_id` 有 FK 指向 `rooms`（那是
// ADR-10 的 CASCADE 機制），所以對不存在的房間下封鎖會丟 23503。Redis 的 Set 從來不管這件事，
// 而那條前置條件一直都成立、只是沒被寫下來——`RoomBanHandler` 先讀房間、檢查房主才動作。
internal sealed class PostgresRoomBanList(IDbContextFactory<ChatDbContext> dbFactory) : IRoomBanList
{
	public async ValueTask<bool> IsBannedAsync(
		string roomId,
		string userId,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// AnyAsync 產生的是 `SELECT EXISTS(...)`，不會把整列拉回來。
		return await db.RoomBans
			.AsNoTracking()
			.AnyAsync(ban => ban.RoomId == roomId && ban.UserId == userId, cancellationToken)
			.ConfigureAwait(false);
	}

	public async ValueTask BanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// `DO NOTHING` 就是介面要求的 idempotent：重複封鎖同一個人跟封鎖一次的結果一樣。走 EF 的
		// Add + SaveChanges 會在第二次撞主鍵丟例外，於是這個方法就得為了「正常的重複呼叫」接例外
		// ——理由跟 PostgresRoomStore.TryCreateAsync 那段一樣，只是這裡連回傳值都不需要。
		await db.Database
			.ExecuteSqlAsync(
				$"""
				INSERT INTO room_bans (room_id, user_id)
				VALUES ({roomId}, {userId})
				ON CONFLICT (room_id, user_id) DO NOTHING
				""",
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async ValueTask UnbanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// 解鎖一個沒被封鎖的人是影響 0 列，不是錯誤——後台的「踢出＋封鎖」與「解鎖」是兩個
		// 獨立按鈕，重複點擊到得了這裡。`ExecuteDeleteAsync` 不需要先把那一列載進來。
		await db.RoomBans
			.Where(ban => ban.RoomId == roomId && ban.UserId == userId)
			.ExecuteDeleteAsync(cancellationToken)
			.ConfigureAwait(false);
	}
}
