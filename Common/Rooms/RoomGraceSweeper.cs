using Chat.Protos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Common.Rooms;

// 寬限期到期的成員需要「N 秒後被移除並廣播離開」，但目前的技術棧沒有排程能力（core NATS
// 沒有延遲投遞，也沒有引入 JetStream），所以用低頻掃描補。
//
// **正確性不依賴這個 sweeper**：名單查詢與 fan-out 在讀取時就濾掉過期成員（ADR-2），
// sweeper 掛掉只會讓「離開」的廣播遲到、Redis 裡累積幽靈成員。
//
// **必須是 idempotent 的**：CommandRouter 是多複本，每個複本都跑自己的 sweeper，同一個過期
// 成員可能被多個複本同時處理。RemoveAsync 重複呼叫是安全的；重複的 RoomMemberLeft 廣播由
// client 容忍（收到不存在成員的離開通知就忽略）。
// internal 就夠：註冊它的 AddRoomGraceSweeper() 就在同一個組件裡。
// （RoomDisconnectHandler 必須是 public，因為宿主要在 AddHandler<T> 裡點名它，把它接到
// 某個 NATS subject 上。）
internal sealed class RoomGraceSweeper(
	IRoomMembership membership,
	RoomBroadcaster broadcaster,
	ILogger<RoomGraceSweeper> logger) : BackgroundService
{
	// 沿用 ConnectionHeartbeatService 的量級。跟 30 秒的寬限期一樣是憑經驗抓的暫定值，
	// 未經負載測試。
	internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(Interval);

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

	internal async ValueTask SweepAsync(CancellationToken cancellationToken)
	{
		IReadOnlyCollection<(string RoomId, string UserId)> expired;

		try
		{
			expired = await membership.ListExpiredAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// 掃描失敗就等下一輪。這一輪的失敗沒有累積效果——過期的成員還在 sorted set 上。
			logger.LogError(ex, "Failed to list expired room memberships.");
			return;
		}

		foreach (var (roomId, userId) in expired)
		{
			try
			{
				await RemoveAsync(roomId, userId, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// 一個房間的失敗不該讓其他房間的過期成員也卡住。
				logger.LogError(ex, "Failed to sweep {UserId} out of {RoomId}.", userId, roomId);
			}
		}
	}

	private async ValueTask RemoveAsync(string roomId, string userId, CancellationToken cancellationToken)
	{
		await membership.RemoveAsync(roomId, userId, cancellationToken).ConfigureAwait(false);

		var remaining = await membership.GetMembersAsync(roomId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ToUsersAsync(
				[.. remaining.Select(member => member.UserId)],
				new RoomMemberLeft { RoomId = roomId, UserId = userId },
				cancellationToken)
			.ConfigureAwait(false);

		logger.LogInformation("Swept {UserId} out of {RoomId} after the grace period.", userId, roomId);
	}
}
