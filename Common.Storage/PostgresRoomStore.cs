using Common.Storage;
using Microsoft.EntityFrameworkCore;

namespace Common.Rooms;

// chat-layer.md ADR-4 的正式儲存。**每個方法都是單一語句**，沒有 read-modify-write、不需要交易
// ——那是 IRoomStore「一次到位、回傳有沒有生效」那條介面約定換來的。
//
// **拿的是 IDbContextFactory 而不是 ChatDbContext**：這個 store 是 singleton（`ChatRetentionSweeper`
// 那類 BackgroundService 也要用同一組介面），而 DbContext 是 scoped 且**不是執行緒安全的**。注入
// scoped 進 singleton 是 captive dependency，DI 會擋下來；就算擋不下來，兩個併發命令共用一個
// DbContext 也會炸。工廠讓每個操作有自己的短命 context，形狀跟先前的 NpgsqlDataSource 一樣。
internal sealed class PostgresRoomStore(IDbContextFactory<ChatDbContext> dbFactory) : IRoomStore
{
	public async ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **一律 AsNoTracking**：這一層讀出來的東西從來不會被改了再存回去（介面上根本沒有那條
		// 路徑），追蹤只是白付快照的成本。
		return await db.Rooms
			.AsNoTracking()
			.FirstOrDefaultAsync(room => room.RoomId == roomId, cancellationToken)
			.ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyCollection<Room>> ListAsync(CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// 沒有 OrderBy：順序不在 IRoomStore 的契約裡（RoomStoreContract 明確不斷言它）。
		// 房間數量的成長由 room-layer.md §8 那條「分頁先不做」承接，不在這裡處理。
		return await db.Rooms.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **這裡刻意不是 `db.Rooms.Add()` + `SaveChangesAsync()`。** EF Core 沒有原生的 upsert，
		// 而「已存在就不覆寫」是本介面唯一不能有競爭的操作：走 EF 的路要嘛先 SELECT 再 INSERT
		// （中間那個窗口正是要防的東西），要嘛靠 SaveChanges 撞主鍵丟 DbUpdateException 再接住
		// ——後者能work，但把控制流建在例外上，而且要拆開 inner exception 判 SqlState 才分得出
		// 「撞主鍵」和「連線斷了」。
		//
		// `ExecuteSqlAsync` 收的是 FormattableString，每個內插值都會變成參數而不是字串拼接。
		var inserted = await db.Database
			.ExecuteSqlAsync(
				$"""
				INSERT INTO rooms (room_id, name, password_hash, owner_user_id, created_at)
				VALUES ({room.RoomId}, {room.Name}, {room.PasswordHash}, {room.OwnerUserId}, {room.CreatedAt})
				ON CONFLICT (room_id) DO NOTHING
				""",
				cancellationToken)
			.ConfigureAwait(false);

		return inserted == 1;
	}

	public async ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// **`ExecuteUpdateAsync` 而不是讀出來改再存**：後者是 read-modify-write，會 lost update，
		// 而且會整列 UPDATE ——房主與建立時間不可變更（Room 的註解說明後台三個流程都靠「房主永遠
		// 不會變」才對 TOCTOU 安全），整列寫回去會靜默拆掉那個前提。這裡產生的 SQL 只動兩個欄位。
		//
		// 房間不存在就是影響 0 列、回 false。
		var updated = await db.Rooms
			.Where(room => room.RoomId == roomId)
			.ExecuteUpdateAsync(
				setters => setters
					.SetProperty(room => room.Name, name)
					.SetProperty(room => room.PasswordHash, passwordHash),
				cancellationToken)
			.ConfigureAwait(false);

		return updated == 1;
	}

	public async ValueTask<bool> TryDeleteAsync(string roomId, CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		// 封鎖名單與訊息靠**資料庫的** `ON DELETE CASCADE` 跟著走（ADR-10）。`ExecuteDeleteAsync`
		// 直接下 DELETE、不經過 change tracker，所以 EF 那套 client-side cascade 完全不介入——
		// 這正是要的：連動由 schema 保證，不是由「記得先把子資料載進記憶體」保證。
		//
		// 影響列數就是守門：兩個併發的 close 只有一個刪得到，所以 RoomClosed 不會廣播兩次。
		var deleted = await db.Rooms
			.Where(room => room.RoomId == roomId)
			.ExecuteDeleteAsync(cancellationToken)
			.ConfigureAwait(false);

		return deleted == 1;
	}
}
