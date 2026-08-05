using Adaptare;
using Chat.Protos;
using Microsoft.Extensions.Logging;

namespace Common.Rooms;

// 訂閱連線層的 events.connection.disconnected，把成員標記成「正在寬限期中」。
//
// 用 IMessageHandler 而不是 IMessageProcessor：這是 fire-and-forget 的事件，沒有回覆，
// 發布端也不等待處理完成。
//
// 這個型別曾經是一個 BackgroundService，自己用原生 INatsConnection 訂閱，理由寫的是「實測
// 混用 AddProcessor 與 AddHandler 時 handler 完全收不到訊息」——**那個結論是錯的**。實測過
// 兩者在同一條註冊鏈裡完全共存（processor 與 handler 都被呼叫）。當時真正壞掉的是發布端，
// 見 ConnectionEventPublisher 的註解。
//
// 事件帶 principal（connection-layer.md ADR-9），所以這裡不需要任何 connectionId → userId
// 的反查；connectionId 仍然要傳下去，因為標記本身要靠它做 fencing（ADR-4）。
public sealed class RoomDisconnectHandler(
	IRoomMembership membership,
	ILogger<RoomDisconnectHandler> logger) : IMessageHandler<byte[]>
{
	public const string Subject = "events.connection.disconnected";

	// queue group 用有層次的名字：NATS 的規則是不同 group 各收到一份、同一個 group 內互相分攤。
	// 未來若有第二個訂閱端沿用同一個名字，兩邊會互搶事件，症狀是「有時候有處理、有時候沒有」。
	public const string QueueGroup = "room.membership";

	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
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
