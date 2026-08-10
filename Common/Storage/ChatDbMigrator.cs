using Dapper;
using Npgsql;

namespace Common.Storage;

// 冪等的啟動時 migration（chat-layer.md 6.6）。不用 EF Core：本系統的查詢只有四種形狀，全部
// 手寫得出來，而 DbContext 生命週期、變更追蹤、以及「產生的 SQL 跟你想的不一樣」都是白付的成本。
//
// **代價已知**：`CREATE TABLE IF NOT EXISTS` 不處理**欄位**變更。真的要改欄位時得引入 DbUp 或
// FluentMigrator，或手動處理。這是當時的取捨，不是疏漏。
//
// schema 由聊天層與房間層共用（ADR-4：同一個資料庫、同一套 migration），所以它住在
// Common/Storage 而不是任一層底下——它不屬於其中任何一層。
//
// DDL 刻意**不寫 schema 名稱**，落在哪個 schema 由連線的 `search_path` 決定。測試因此可以拿
// 同一份 DDL 建到一個獨立 schema 去跑，不必回頭 TRUNCATE 應用真正在用的那幾張表。
public sealed class ChatDbMigrator(NpgsqlDataSource dataSource)
{
	// `CommandRouter` 會開複本（階段 B 之後），而**併發的 `CREATE TABLE IF NOT EXISTS` 在
	// Postgres 上會撞 duplicate object（42P07）**——`IF NOT EXISTS` 擋的是「已經存在」，不是
	// 「另一個交易正在建」。advisory lock 讓那個競爭消失，成本是啟動時多一次往返。
	private const long LockKey = 0x_C4A7_DB_01;

	private const string Ddl =
		"""
		CREATE TABLE IF NOT EXISTS rooms (
			room_id       text        PRIMARY KEY,
			name          text        NOT NULL,
			password_hash text        NULL,
			owner_user_id text        NOT NULL,
			created_at    timestamptz NOT NULL
		);

		CREATE TABLE IF NOT EXISTS room_bans (
			room_id text NOT NULL REFERENCES rooms(room_id) ON DELETE CASCADE,
			user_id text NOT NULL,
			PRIMARY KEY (room_id, user_id)
		);
		""";

	public async Task MigrateAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

		await connection
			.ExecuteAsync(new CommandDefinition(
				"SELECT pg_advisory_lock(@LockKey)",
				new { LockKey },
				cancellationToken: cancellationToken))
			.ConfigureAwait(false);

		try
		{
			await connection
				.ExecuteAsync(new CommandDefinition(Ddl, cancellationToken: cancellationToken))
				.ConfigureAwait(false);
		}
		finally
		{
			// 連線關掉 advisory lock 也會釋放，但明確解鎖讓「同一條連線接著做別的事」是安全的。
			await connection
				.ExecuteAsync(new CommandDefinition(
					"SELECT pg_advisory_unlock(@LockKey)",
					new { LockKey },
					cancellationToken: cancellationToken))
				.ConfigureAwait(false);
		}
	}
}
