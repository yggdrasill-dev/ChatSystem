using Chat.Protos;
using MessagePacket = Chat.Protos.ChatMessage;

namespace Common.Chat.Handlers;

// 領域型別 ↔ wire 型別的唯一對應處。
//
// **`Common.Chat.ChatMessage`（領域）與 `Chat.Protos.ChatMessage`（wire）同名**，所以這裡
// 需要一個別名。這是刻意留著的：兩個型別代表同一個概念的兩種形態，硬要改名其中一個只會
// 讓「哪一個才是領域概念」變模糊。需要同時提到兩者的檔案只有這裡跟
// ChatLayerServiceCollectionExtensions（要 `AddOutboundPacket<MessagePacket>`）——handler
// 靠泛型推論，一次都不用寫。
internal static class ChatPacket
{
	public static ChatOperationReply Reply(ChatOperationReply.Types.Status status, string roomId) =>
		new() { Status = status, RoomId = roomId };

	public static MessagePacket Of(ChatMessage message) =>
		new()
		{
			RoomId = message.RoomId,
			OrderKey = message.OrderKey,
			SenderUserId = message.SenderUserId,
			SenderDisplayName = message.SenderDisplayName,
			Body = message.Body,
			// order_key 與 sent_at 是兩個欄位：前者在突發時可能借用未來的微秒，後者不會。
			// 顯示時間一律用這個（ADR-2）。
			SentAtUnixMs = message.SentAt.ToUnixTimeMilliseconds()
		};
}
