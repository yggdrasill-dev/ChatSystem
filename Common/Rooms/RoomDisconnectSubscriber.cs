using Chat.Protos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Common.Rooms;

// 訂閱連線層的 events.connection.disconnected，把成員標記成「正在寬限期中」。
//
// **為什麼不用 Adaptare 的 AddHandler**：CommandRouter 的那條註冊鏈裡已經有一個
// AddProcessor（command.inbound 的 request/reply）。實測混用 AddProcessor 與 AddHandler 時，
// processor 會動、handler 完全收不到訊息——Dispatcher 只有兩個 AddHandler 所以沒事。
// 這個訂閱因此直接用 NATS client 自己訂，不經過 Adaptare：範圍小、行為完全在我們手上，
// 而且不用去動已經在動的那條 inbound 路徑。
//
// 事件帶 principal（connection-layer.md ADR-9），所以這裡不需要任何 connectionId → userId
// 的反查；connectionId 仍然要傳下去，因為標記本身要靠它做 fencing（ADR-4）。
//
// queue group 用有層次的名字：NATS 的規則是不同 group 各收到一份、同一個 group 內互相分攤。
// 未來若有第二個訂閱端沿用同一個名字，兩邊會互搶事件，症狀是「有時候有處理、有時候沒有」。
internal sealed class RoomDisconnectSubscriber(
	INatsConnection connection,
	IRoomMembership membership,
	ILogger<RoomDisconnectSubscriber> logger) : BackgroundService
{
	internal const string Subject = "events.connection.disconnected";
	internal const string QueueGroup = "room.membership";

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("Subscribing to {Subject} as {QueueGroup}.", Subject, QueueGroup);

		try
		{
			await foreach (var message in connection
				.SubscribeAsync<byte[]>(Subject, queueGroup: QueueGroup, cancellationToken: stoppingToken)
				.ConfigureAwait(false))
			{
				await HandleAsync(message.Data, stoppingToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// 關站，不是錯誤。
		}
	}

	internal async ValueTask HandleAsync(byte[]? data, CancellationToken cancellationToken)
	{
		try
		{
			if (data is null || data.Length == 0)
				return;

			var message = ConnectionDisconnected.Parser.ParseFrom(data);

			// 沒有 principal 的連線不該存在（handshake 一定驗過），但事件是跨 process 來的，
			// 寧可忽略也不要在 Redis 上寫出一個空 userId 的成員。
			if (string.IsNullOrEmpty(message.Principal))
			{
				logger.LogWarning(
					"Disconnected event for {ConnectionId} had no principal, ignoring.",
					message.ConnectionId);

				return;
			}

			logger.LogInformation("Marking {ConnectionId} disconnected for the grace period.", message.ConnectionId);

			// 標記失敗（不在任何房間、或 fencing 不符）是常態，不是錯誤：多數連線根本沒進過房間。
			await membership
				.MarkDisconnectedAsync(message.Principal, message.ConnectionId, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// 一則事件處理失敗不該讓整個訂閱掉線。事件本來就是 best-effort，而寬限期的正確性
			// 不依賴它（ADR-2 的「讀取時過濾」）。
			logger.LogError(ex, "Failed to handle a disconnected event.");
		}
	}
}
