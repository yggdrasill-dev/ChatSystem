using System.Data.Common;
using System.Text.Json;
using Common.Rooms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace E2E.Tests;

// **§9 測試分層的最後一條欠帳：keyset 分頁在稀疏鍵上真的走主鍵的 index scan。**
//
// 它守的性質是**翻一頁的成本不隨房間的訊息量成長**。契約測試看不到這件事——它們只看回傳的
// 資料，而「掃完 5000 列再丟掉 4949 列」跟「只讀 51 列」回傳的頁面一模一樣。實際驗過：把
// `Take(limit + 1)` 從 SQL 搬到 C# 這一側，11 條訊息 store 的契約測試全部照過，只有這一條紅。
//
// 斷言的形狀跟其他測試都不一樣（要看 planner 選了什麼），所以它沒有辦法進契約：in-memory 的
// 替身沒有 planner。這跟「刪房把訊息一起帶走」進不了契約是同一類理由——**只有真資料庫給得起的
// 性質**。
//
// **它沒有守住 §9 原本以為它會守的那件事，而那是因為那件事不存在。** §9（與這個 store 裡的
// 註解）原本寫「把游標翻譯改回 `(@before = 0 OR order_key < @before)` 的話 planner 會改成
// seq scan，而所有測試都會照過」。加這條測試時第一個驗的就是那個變異，**結果它照樣綠**：EF 在
// 翻譯階段就把那個 OR 折疊掉了，Postgres 從來沒看到它。詳情記在 `PostgresChatMessageStore`
// 的註解與 chat-layer.md §11。
//
// 閘門是執行期的 `[E2EFact]` 而不是 csproj 的 Compile Remove：這個類別自己就是 E2E 專屬的，
// 沒有繼承任何帶普通 `[Fact]` 的基底（那才是那兩份契約殼需要編譯期閘門的原因）。
[Collection(AppHostCollection.Name)]
public sealed class ChatMessageQueryPlanTests(AppHostFixture fixture) : IAsyncLifetime
{
	// **少了不行**：planner 對一張只有幾十列的表一定選 seq scan，而那是正確的選擇（整張表就一兩頁），
	// 所以列數太少的話這條測試會紅在自己的前提上。5000 列足夠讓 index scan + LIMIT 明顯地便宜，
	// 又還能用一句 `generate_series` 在幾十毫秒內灌完。
	private const int SeededMessages = 5000;

	private const int PageSize = 50;

	private readonly CapturingCommandInterceptor m_Commands = new();

	private ChatDbProbe m_Probe = null!;

	public async Task InitializeAsync()
	{
		if (!E2EFactAttribute.Enabled)
			return;

		m_Probe = await fixture.ConnectChatDbAsync(m_Commands);
	}

	public async Task DisposeAsync()
	{
		if (m_Probe is not null)
			await m_Probe.DisposeAsync();
	}

	[E2EFact]
	public async Task GetPage_ReadsThroughThePrimaryKeyIndex_AndStopsAtThePageSize()
	{
		await SeedAsync();

		// 前提檢查：這條測試的價值完全建立在「表很大」上。**必須在 Clear() 之前**——它自己也是一
		// 條查詢，攔到的話下面那個 Single() 就會看到兩條 command。
		Assert.Equal(SeededMessages, await CountMessagesAsync());

		// seed 與前提檢查都會經過 interceptor，所以在量測的那一次呼叫之前清掉。
		m_Commands.Clear();

		var page = await m_Probe.Messages.GetPageAsync("room-1", 0, PageSize);

		// 另一半的前提：只讀了一頁。
		Assert.Equal(PageSize, page.Messages.Count);
		Assert.True(page.HasMore);

		var nodes = await ExplainAsync(m_Commands.Single());

		// seq scan 就是「每翻一頁都掃過整個房間的歷史」。這一條紅掉時資料仍然是對的，只是變慢——
		// **這整個類別存在的理由就是那種只有資料量長大之後才會痛的錯誤。**
		Assert.DoesNotContain("Seq Scan", nodes.Select(NodeType));

		// 走的是主鍵 `(room_id, order_key)`，方向是 backward（`ORDER BY order_key DESC`）。
		//
		// **索引的名字是問資料庫拿的，不是寫死的。** 寫死會把 EF 的命名慣例（`PK_messages`）變成
		// 這條測試的一部分——而那個名字跟被驗的性質毫無關係，改個 constraint 名稱就會紅在一個
		// 誤導人的地方。順帶一提原本手寫 DDL 時代它叫 `messages_pkey`。
		var primaryKeyIndex = await PrimaryKeyIndexNameAsync();
		var scan = Assert.Single(nodes, node => IndexName(node) == primaryKeyIndex);

		// **這是這條測試真正在守的性質**：翻一頁的成本不隨房間的訊息量成長。5000 列裡只有 51 列
		// 被實際讀出來（`limit + 1`，多取一筆用來判斷 has_more）。node type 那條斷言可以被一個
		// 「能用索引但還是掃完整段範圍」的計畫騙過去，這條不行。
		//
		// 讀成 double 不是 int：PostgreSQL 17 之後 `Actual Rows` 是帶小數的（多次 loop 的平均）。
		Assert.InRange(scan.GetProperty("Actual Rows").GetDouble(), 0, PageSize + 1);
	}

	private async Task SeedAsync()
	{
		// FK 要求房間存在（`messages.room_id` → `rooms`，ADR-10 的 CASCADE 機制）。用 store 而不是
		// 手寫 INSERT，跟契約測試的前置條件走同一條路。
		Assert.True(await m_Probe.Store.TryCreateAsync(new Room(
			"room-1",
			"Lobby",
			null,
			"owner",
			DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000))));

		await using var db = await m_Probe.Db.CreateDbContextAsync();

		// order_key 遞增地灌，跟真實的寫入順序一致（append-only）。
		await db.Database.ExecuteSqlAsync(
			$"""
			INSERT INTO messages (room_id, order_key, sender_user_id, sender_display_name, body, sent_at)
			SELECT 'room-1', g, 'alice', 'Alice', 'message ' || g, now()
			FROM generate_series(1, {SeededMessages}) AS g
			""");

		// **不 ANALYZE 的話這條測試會看時機決定紅綠**：planner 用的是統計資訊，而剛灌完的表在
		// autovacuum 跑到之前對它來說是空的——對一張「零列」的表，seq scan 永遠是最便宜的計畫。
		await db.Database.ExecuteSqlRawAsync("ANALYZE messages");
	}

	private async Task<int> CountMessagesAsync()
	{
		await using var db = await m_Probe.Db.CreateDbContextAsync();

		return await db.Messages.CountAsync();
	}

	// `'messages'::regclass` 靠連線的 search_path 解析，所以問到的是 probe 那個 schema 裡的表，
	// 不是 app 正在用的 `public.messages`。
	private async Task<string> PrimaryKeyIndexNameAsync()
	{
		await using var db = await m_Probe.Db.CreateDbContextAsync();
		var connection = (NpgsqlConnection)db.Database.GetDbConnection();

		await connection.OpenAsync();

		await using var command = connection.CreateCommand();
		command.CommandText =
			"""
			SELECT i.relname
			FROM pg_index x
			JOIN pg_class i ON i.oid = x.indexrelid
			WHERE x.indrelid = 'messages'::regclass AND x.indisprimary
			""";

		return await command.ExecuteScalarAsync() as string
			?? throw new InvalidOperationException("messages 沒有主鍵索引。");
	}

	// 把 store 剛剛送出去的那條 command 原封不動地包進 `EXPLAIN`，參數照抄。
	//
	// **參數不能就地換成字面值**：那會讓 planner 拿到跟 production 不同的資訊（EF 送的是參數化
	// 查詢），而「參數化之後 planner 選了什麼」正是這條測試要問的。
	private async Task<IReadOnlyList<JsonElement>> ExplainAsync(CapturedCommand captured)
	{
		await using var db = await m_Probe.Db.CreateDbContextAsync();
		var connection = (NpgsqlConnection)db.Database.GetDbConnection();

		await connection.OpenAsync();

		await using var command = connection.CreateCommand();

		// ANALYZE：要的不只是計畫的形狀，還有「實際讀了幾列」。這句是 SELECT，執行它沒有副作用。
		command.CommandText = $"EXPLAIN (ANALYZE, FORMAT JSON) {captured.CommandText}";

		foreach (var (name, value) in captured.Parameters)
			command.Parameters.Add(
				// EF + Npgsql 可能產生具名（`@__p_0`）或位置（`$1`）參數，兩種都照抄得起來：
				// 名字是空的就當位置參數加進去，順序跟原本的 command 一致。
				string.IsNullOrEmpty(name)
					? new NpgsqlParameter { Value = value ?? DBNull.Value }
					: new NpgsqlParameter(name, value ?? DBNull.Value));

		var json = await command.ExecuteScalarAsync() as string
			?? throw new InvalidOperationException("EXPLAIN 沒有回傳計畫。");

		using var document = JsonDocument.Parse(json);

		// `EXPLAIN (FORMAT JSON)` 回的是一個陣列，每個元素一個 statement，各有一個 "Plan" 樹。
		return [.. document.RootElement.EnumerateArray().SelectMany(statement => Flatten(statement.GetProperty("Plan")))];
	}

	// 計畫是一棵樹（Limit → Index Scan），斷言只關心「這棵樹裡有沒有出現某個節點」，所以攤平。
	// JsonElement 在 document 被 dispose 之後就不能用了，所以這裡要 Clone。
	private static IEnumerable<JsonElement> Flatten(JsonElement node)
	{
		yield return node.Clone();

		if (!node.TryGetProperty("Plans", out var children))
			yield break;

		foreach (var child in children.EnumerateArray())
			foreach (var descendant in Flatten(child))
				yield return descendant;
	}

	private static string? NodeType(JsonElement node) =>
		node.TryGetProperty("Node Type", out var value) ? value.GetString() : null;

	private static string? IndexName(JsonElement node) =>
		node.TryGetProperty("Index Name", out var value) ? value.GetString() : null;
}

public sealed record CapturedCommand(string CommandText, IReadOnlyList<(string Name, object? Value)> Parameters);

// **測試要 EXPLAIN 的是 production 送出去的那條 SQL。** 自己在測試裡重寫一次同樣的 LINQ 會通過，
// 但它證明的是「我寫的這條查詢走索引」，而不是「store 那條走索引」——兩者可以在無聲無息中分岔。
// EF 的 interceptor 是唯一拿得到真正那條 command 的地方。
public sealed class CapturingCommandInterceptor : DbCommandInterceptor
{
	private readonly List<CapturedCommand> m_Captured = [];

	public CapturedCommand Single()
	{
		lock (m_Captured)
			return Assert.Single(m_Captured);
	}

	public void Clear()
	{
		lock (m_Captured)
			m_Captured.Clear();
	}

	public override InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result)
	{
		Capture(command);

		return result;
	}

	// store 走的是 `ToListAsync()`，所以實際被呼叫的是這一個；同步版留著是因為
	// 「哪一個會被呼叫」取決於呼叫端，而漏掉一個的症狀是 Single() 說一條都沒攔到。
	public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command,
		CommandEventData eventData,
		InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		Capture(command);

		return ValueTask.FromResult(result);
	}

	private void Capture(DbCommand command)
	{
		// 參數要在這裡就抄下來：command 執行完就被 dispose 了。
		var parameters = command.Parameters
			.Cast<DbParameter>()
			.Select(parameter => (parameter.ParameterName, parameter.Value))
			.ToArray();

		lock (m_Captured)
			m_Captured.Add(new CapturedCommand(command.CommandText, parameters));
	}
}
