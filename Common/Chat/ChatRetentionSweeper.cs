using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common.Chat;

// 保留期限的清理（chat-layer.md ADR-6）：每天一輪，每批 5000 列，刪到當輪沒東西可刪為止。
//
// **必須是 idempotent 的**，理由跟 RoomGraceSweeper 完全一樣：CommandRouter 是多複本，每個
// 複本都跑自己的 sweeper。「刪掉 sent_at 早於 cutoff 的訊息」天生 idempotent，兩個複本同時
// 跑最多是白做工。
//
// **正確性不依賴它**：掛掉的後果是磁碟成長，不是查詢結果錯誤。所以刻意**沒有**對應的
// 「讀取時過濾」機制——過期但還沒被刪的訊息仍然查得到，那是可接受的。
//
// 它**不負責**房間被刪掉時的清理，那條路徑是 ON DELETE CASCADE（ADR-10）。
internal sealed class ChatRetentionSweeper(
	IChatMessageStore store,
	ChatRetention retention,
	TimeProvider timeProvider,
	ILogger<ChatRetentionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(retention.SweepInterval, timeProvider);

		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
				await SweepAsync(stoppingToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// 關站，不是錯誤。
		}
	}

	internal async ValueTask<int> SweepAsync(CancellationToken cancellationToken)
	{
		var cutoff = timeProvider.GetUtcNow() - retention.Period;
		var total = 0;

		try
		{
			int deleted;

			// 分批而不是一次 DELETE：一次刪 90 天前的全部訊息會變成長交易（鎖競爭、
			// autovacuum 追不上造成的 bloat）。分區表是後續的條件觸發決定，見 ADR-6。
			do
			{
				deleted = await store
					.DeleteOlderThanAsync(cutoff, retention.BatchSize, cancellationToken)
					.ConfigureAwait(false);

				total += deleted;
			}
			while (deleted >= retention.BatchSize);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// 這一輪的失敗沒有累積效果——過期的訊息還在，下一輪會再看到。
			logger.LogError(ex, "Failed to sweep chat messages older than {Cutoff}.", cutoff);
		}

		if (total > 0)
			logger.LogInformation("Swept {Count} chat message(s) older than {Cutoff}.", total, cutoff);

		return total;
	}
}
