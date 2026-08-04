using Adaptare;
using Chat.Protos;
using Microsoft.Extensions.Logging;

namespace Common.Rooms;

// 訂閱連線層的 events.connection.disconnected，把成員標記成「正在寬限期中」。
//
// 事件帶 principal（connection-layer.md ADR-9），所以這裡不需要任何 connectionId → userId
// 的反查；connectionId 仍然要傳下去，因為標記本身要靠它做 fencing（ADR-4）。
//
// **queue group 名稱要跟其他訂閱端區隔**：NATS 的規則是不同 group 各收到一份、同一個 group
// 內互相分攤。未來若有第二個訂閱端沿用同一個名字，兩邊會互搶事件，症狀是「有時候有處理、
// 有時候沒有」——這種錯誤在 diff 上看不出來。
public sealed class RoomDisconnectHandler(
	IRoomMembership membership,
	ILogger<RoomDisconnectHandler> logger) : IMessageHandler<byte[]>
{
	public async ValueTask HandleAsync(
		string subject,
		byte[] data,
		IEnumerable<MessageHeaderValue>? headerValues,
		CancellationToken cancellationToken = default)
	{
		var message = ConnectionDisconnected.Parser.ParseFrom(data);

		// 沒有 principal 的連線不存在（handshake 一定驗過），但事件是跨 process 來的，
		// 防一手比在 Redis 上寫出一個空 userId 的成員好。
		if (string.IsNullOrEmpty(message.Principal))
		{
			logger.LogWarning("Disconnected event for {ConnectionId} had no principal, ignoring.", message.ConnectionId);
			return;
		}

		logger.LogInformation(
			"Marking {ConnectionId} disconnected for the grace period.",
			message.ConnectionId);

		// 標記失敗（不在任何房間、或 fencing 不符）是常態，不是錯誤：多數連線根本沒進過房間。
		await membership
			.MarkDisconnectedAsync(message.Principal, message.ConnectionId, cancellationToken)
			.ConfigureAwait(false);
	}
}
