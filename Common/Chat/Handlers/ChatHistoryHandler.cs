using Chat.Protos;
using Common.Protocol;
using Common.Rooms;

namespace Common.Chat.Handlers;

internal sealed class ChatHistoryHandler(
	IRoomMembership membership,
	IChatMessageStore store,
	RoomBroadcaster broadcaster) : IPacketHandler<ChatHistoryRequest>
{
	// 暫定值。50 在 chat-layer.md §9 的清單裡；上限是實作時補的——沒有上限的話 client 可以
	// 一次要求拉完整個房間的歷史，而 ADR-5 已經說明這條查詢會佔住那條連線的順序通道。
	internal const int DefaultLimit = 50;
	internal const int MaxLimit = 100;

	public async ValueTask HandleAsync(
		CommandContext context,
		ChatHistoryRequest message,
		CancellationToken cancellationToken = default)
	{
		// 授權就是「你現在在這間房嗎」。命令不帶 room_id，所以**沒有辦法查詢自己不在的房間**
		// ——那個限制是刻意的（ADR-5），它讓「讀取任意房間歷史訊息」這個漏洞不可能存在。
		var roomId = await membership.GetCurrentRoomAsync(context.Principal, cancellationToken).ConfigureAwait(false);

		if (roomId is null)
		{
			await broadcaster
				.ReplyAsync(
					context,
					ChatPacket.Reply(ChatOperationReply.Types.Status.NotInRoom, string.Empty),
					cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		// 0、負數、超過上限都夾到範圍內，不回錯誤（6.2）：分頁參數壞掉是 client 的 bug，
		// 讓它拿到一頁合理的資料比讓它收到一個錯誤碼有用。
		var limit = message.Limit is <= 0 or > MaxLimit ? DefaultLimit : message.Limit;
		var before = Math.Max(message.BeforeOrderKey, 0);

		var page = await store.GetPageAsync(roomId, before, limit, cancellationToken).ConfigureAwait(false);

		var history = new ChatHistory { RoomId = roomId, HasMore = page.HasMore };
		history.Messages.AddRange(page.Messages.Select(ChatPacket.Of));

		// 回覆直接送回 context.ConnectionId，不查 Presence——Supersede 之後 Presence 可能已經
		// 指向別條連線（沿用 RoomBroadcaster 既有的理由）。
		await broadcaster.ReplyAsync(context, history, cancellationToken).ConfigureAwait(false);
	}
}
