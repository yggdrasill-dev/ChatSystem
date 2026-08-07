using Chat.Protos;
using Common.Chat;
using Common.Chat.Handlers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MessagePacket = Chat.Protos.ChatMessage;

namespace Microsoft.Extensions.DependencyInjection;

public static class ChatLayerServiceCollectionExtensions
{
	// 聊天層的儲存與發號。
	//
	// **階段 A：儲存與限流都是程序內的替身**，見下面兩處標記。這一層的**行為**是完整的
	// （先存後廣播、keyset 分頁、排序鍵單調唯一、限流、保留期清理），但它還不能上線——
	// 訊息不持久化、限流不跨複本。階段 B 換成 PostgreSQL + Redis，換掉的就是那兩行
	// （chat-layer.md ADR-4／ADR-7）。
	public static IServiceCollection AddChatStore(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton(ChatRetention.Default);
		services.TryAddSingleton(ChatRateLimit.Default);

		// **singleton 不是 scoped**：單調守衛的狀態必須是整個 process 共用的。註冊成 scoped 會
		// 讓每則命令拿到一個新的守衛，那個 CAS 迴圈就白寫了（chat-layer.md 6.7）。
		services.AddSingleton<IMessageSequencer, MonotonicMicrosecondSequencer>();

		// 階段 A：跨複本不成立，且字典沒有淘汰。階段 B 換 Redis INCR + EXPIRE。
		services.AddSingleton<IChatRateLimiter, InMemoryChatRateLimiter>();

		// 階段 A：process 重啟訊息就消失。階段 B 換 PostgresChatMessageStore。
		return services.AddSingleton<IChatMessageStore, InMemoryChatMessageStore>();
	}

	// 聊天層向協定層註冊自己的命令與下行訊息型別。每個 subject 字面值在整個 codebase 只出現
	// 這一次。需要先呼叫 AddChatStore()、AddPacketRegistry()、房間層的 AddRoomPackets()
	// （共用 RoomBroadcaster 與 IRoomMembership），以及身分層的 AddIdentityStores(...)。
	public static IServiceCollection AddChatPackets(this IServiceCollection services)
	{
		services.AddPacketHandler<SendChatMessageRequest, ChatSendHandler>("chat.send");
		services.AddPacketHandler<ChatHistoryRequest, ChatHistoryHandler>("chat.history");

		// MessagePacket 是 Chat.Protos.ChatMessage 的別名——領域的 Common.Chat.ChatMessage 同名，
		// 理由見 Common/Chat/Handlers/ChatPacket.cs。
		services.AddOutboundPacket<MessagePacket>("chat.message");
		services.AddOutboundPacket<ChatHistory>("chat.history.reply");

		return services.AddOutboundPacket<ChatOperationReply>("chat.reply");
	}

	// 保留期限的清理（ADR-6）。跟 AddRoomMembershipMaintenance() 是同一種形狀。
	public static IServiceCollection AddChatRetention(this IServiceCollection services) =>
		services.AddHostedService<ChatRetentionSweeper>();
}
