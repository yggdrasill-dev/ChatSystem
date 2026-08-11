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
	// **B1：訊息已經在 PostgreSQL 上**（chat-layer.md ADR-4）。需要先呼叫
	// builder.AddNpgsqlDbContext<ChatDbContext>("chat-db")，跟房間層共用同一個 DbContext 與同一套 migration。
	//
	// **限流還是程序內的替身**（下面那行），所以這個 process 仍然不能算完整上線——限流不跨複本。
	// 那是階段 B 的 (2)，換掉的是一行實作（ADR-7）。
	public static IServiceCollection AddChatStore(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton(ChatRetention.Default);
		services.TryAddSingleton(ChatRateLimit.Default);

		// **singleton 不是 scoped**：單調守衛的狀態必須是整個 process 共用的。註冊成 scoped 會
		// 讓每則命令拿到一個新的守衛，那個 CAS 迴圈就白寫了（chat-layer.md 6.7）。
		services.AddSingleton<IMessageSequencer, MonotonicMicrosecondSequencer>();

		// 階段 B 的 (2)：跨複本不成立，且字典沒有淘汰。換 Redis INCR + EXPIRE。
		services.AddSingleton<IChatRateLimiter, InMemoryChatRateLimiter>();

		// **不需要容器的測試因此要自己覆寫這一行**（Integration.Tests/CommandFlowHost.cs）——
		// 階段 A 那個「聊天層在 Direct 路徑上完全不需要替身」的巧合到這裡結束了。
		return services.AddSingleton<IChatMessageStore, PostgresChatMessageStore>();
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
