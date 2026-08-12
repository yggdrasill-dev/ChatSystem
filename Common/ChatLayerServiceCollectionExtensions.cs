using Chat.Protos;
using Common.Chat;
using Common.Chat.Handlers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using MessagePacket = Chat.Protos.ChatMessage;

namespace Microsoft.Extensions.DependencyInjection;

public static class ChatLayerServiceCollectionExtensions
{
	// 聊天層**程序內**的元件：設定與發號。
	//
	// **這裡沒有任何儲存，也沒有限流**——`IChatMessageStore` 的 PostgreSQL 實作住在
	// `Common.Storage`（那邊的 `AddChatDb()`），`IChatRateLimiter` 要 Redis（下面的
	// `AddChatRateLimiting()`）。先前這個方法叫 `AddChatStore()`，抽走儲存之後那個名字就只剩
	// 誤導了。**留在這裡的東西的共同性質是「不需要任何外部資源」**，所以這個方法沒有參數。
	public static IServiceCollection AddChatCore(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton(ChatRetention.Default);
		services.TryAddSingleton(ChatRateLimit.Default);

		// **singleton 不是 scoped**：單調守衛的狀態必須是整個 process 共用的。註冊成 scoped 會
		// 讓每則命令拿到一個新的守衛，那個 CAS 迴圈就白寫了（chat-layer.md 6.7）。
		return services.AddSingleton<IMessageSequencer, MonotonicMicrosecondSequencer>();
	}

	// 限流（ADR-7）。需要先呼叫 builder.AddKeyedRedisClient(redisServiceKey) 與 AddChatCore()
	// （`ChatRateLimit` 在那裡）。
	//
	// **刻意跟 AddChatCore() 分開，而不是收一個 redisServiceKey 參數。** 兩個理由：
	//   - 聊天層需要一顆 Redis 這件事因此在 CommandRouter/Program.cs 上是自己一行、直接看得見的，
	//     跟房間層橫跨兩個儲存被拆成 AddChatDb() + AddRoomMembership() 兩行是同一個決定。
	//   - Integration.Tests 要的是「不呼叫這個方法、自己註冊 in-memory 替身」，那是**取代**；
	//     合在一起的話它只能後註冊蓋掉前註冊，變成要讀兩個地方才知道真正生效的是哪一個。
	public static IServiceCollection AddChatRateLimiting(this IServiceCollection services, string redisServiceKey) =>
		services.AddSingleton<IChatRateLimiter>(sp => new RedisChatRateLimiter(
			sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<ChatRateLimit>()));

	// 聊天層向協定層註冊自己的命令與下行訊息型別。每個 subject 字面值在整個 codebase 只出現
	// 這一次。需要先呼叫 AddChatCore() 與 AddChatDb()、AddChatRateLimiting(...)、
	// AddPacketRegistry()、房間層的 AddRoomPackets()（共用 RoomBroadcaster 與 IRoomMembership），
	// 以及身分層的 AddIdentityStores(...)。
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
