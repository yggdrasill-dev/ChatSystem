using Microsoft.EntityFrameworkCore;

namespace Common.Storage;

// 啟動時把 schema 帶到最新（chat-layer.md 6.6）。**B1 之後從手寫的 `CREATE TABLE IF NOT EXISTS`
// 換成 EF Core Migrations**，換掉的正是 6.6 當初寫下來的那條代價：那個寫法不處理欄位變更。
//
// 現在 `Storage/Migrations/` 底下每一次 schema 變更都是一個有序、可 review、可回退的檔案，
// `__EFMigrationsHistory` 記錄哪些跑過了。**重跑是安全的**：已套用的 migration 會被跳過。
//
// schema 由聊天層與房間層共用（ADR-4：同一個資料庫、同一套 migration），所以它住在
// Common/Storage 而不是任一層底下——它不屬於其中任何一層。
public sealed class ChatDbMigrator(IDbContextFactory<ChatDbContext> dbFactory)
{
	// `CommandRouter` 會開複本，而**併發的 migration 在 Postgres 上會撞 duplicate object（42P07）**
	// ——那是手寫 DDL 時代實際踩過的。EF Core 的 provider 自己也有一層 migration lock，但這一條
	// 不依賴那個行為：它是這個 repo 已經驗證過的保證，而多一次 advisory lock 的成本是啟動時一次
	// 往返。**鎖必須跟 migration 在同一條連線上**，所以這裡明確 Open/Close 而不是讓 EF 自己開。
	private const long LockKey = 0x_C4A7_DB_01;

	public async Task MigrateAsync(CancellationToken cancellationToken = default)
	{
		await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

		await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await db.Database
				.ExecuteSqlAsync($"SELECT pg_advisory_lock({LockKey})", cancellationToken)
				.ConfigureAwait(false);

			await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// 連線關掉 advisory lock 也會釋放，但明確解鎖讓「同一條連線接著做別的事」是安全的。
			await db.Database
				.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", cancellationToken)
				.ConfigureAwait(false);

			await db.Database.CloseConnectionAsync().ConfigureAwait(false);
		}
	}
}
