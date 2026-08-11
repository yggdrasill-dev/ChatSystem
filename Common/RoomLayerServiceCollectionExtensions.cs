using Chat.Protos;
using Common.Rooms;
using Common.Rooms.Handlers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class RoomLayerServiceCollectionExtensions
{
	// **房間層橫跨兩個儲存**（chat-layer.md ADR-4）：房間與封鎖名單是持久資料，住 Postgres；
	// 成員名單是暫時狀態（成員 hash、寬限期的 Sorted Set、userId → roomId 指向），留 Redis。
	//
	// 需要先呼叫 builder.AddNpgsqlDbContext<ChatDbContext>("chat-db") 與 builder.AddKeyedRedisClient(redisServiceKey)。
	// Redis 那邊用 keyed service 是因為同一個 process 會有多個邏輯名稱（AppHost 目前把它們都
	// 指向同一顆實體 Redis，見那裡的註解）。
	public static IServiceCollection AddRoomStore(this IServiceCollection services, object redisServiceKey)
	{
		services.AddSingleton<IRoomStore, PostgresRoomStore>();
		services.AddSingleton<IRoomBanList, PostgresRoomBanList>();

		// 成員名單為什麼留 Redis：TTL 與寬限期的 Sorted Set 語意放進關聯式資料庫會變難看也變慢
		// （ADR-4）。**先前這裡寫的理由是「要跟房間資料跨 key 一起操作」，那句話是錯的**——
		// RedisRoomMembership 那兩段 Lua 動的是 Members / Grace / UserRoom，一個都不碰
		// {rooms}:room:*。`{rooms}` 這個 hash tag 仍然需要，但要的是成員名單自己那幾個 key
		// 落在同一個 slot，跟房間資料無關。
		services.TryAddSingleton(TimeProvider.System);

		return services.AddSingleton<IRoomMembership>(sp =>
			new RedisRoomMembership(
				sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey),
				sp.GetRequiredService<TimeProvider>()));
	}

	// 房間層向協定層註冊自己的命令與下行訊息型別。每個 subject 字面值在整個 codebase 只出現
	// 這一次。需要先呼叫 AddRoomStore(...)、AddPacketRegistry()，以及身分層的
	// AddIdentityStores(...)——fan-out 要靠 IPresenceDirectory 把 userId 換成 connectionId。
	public static IServiceCollection AddRoomPackets(this IServiceCollection services)
	{
		// 沒有 per-command 狀態（context 是參數傳進去的），而且 sweeper 這個 singleton
		// 的 BackgroundService 也要用它，所以是 singleton 不是 scoped。
		services.AddSingleton<RoomBroadcaster>();

		services.AddPacketHandler<CreateRoomRequest, RoomCreateHandler>("room.create");
		services.AddPacketHandler<JoinRoomRequest, RoomJoinHandler>("room.join");
		services.AddPacketHandler<LeaveRoomRequest, RoomLeaveHandler>("room.leave");
		services.AddPacketHandler<ListRoomsRequest, RoomListHandler>("room.list");
		services.AddPacketHandler<KickMemberRequest, RoomKickHandler>("room.kick");
		services.AddPacketHandler<BanMemberRequest, RoomBanHandler>("room.ban");
		services.AddPacketHandler<CloseRoomRequest, RoomCloseHandler>("room.close");
		services.AddPacketHandler<UpdateRoomRequest, RoomUpdateHandler>("room.update");

		services.AddOutboundPacket<RoomOperationReply>("room.reply");
		services.AddOutboundPacket<RoomJoined>("room.joined");
		services.AddOutboundPacket<RoomMemberJoined>("room.member.joined");
		services.AddOutboundPacket<RoomMemberLeft>("room.member.left");
		services.AddOutboundPacket<RoomKicked>("room.kicked");
		services.AddOutboundPacket<RoomClosed>("room.closed");

		return services.AddOutboundPacket<RoomList>("room.list.reply");
	}

	// 寬限期的後半：掃掉到期的成員並廣播離開。
	//
	// 前半（訂閱斷線事件、把成員標記進寬限期）是 RoomDisconnectHandler，刻意**不**在這裡註冊
	// ——它是一個 Adaptare 的 IMessageHandler，得由宿主掛進自己那條唯一的 AddNatsMessageQueue
	// 鏈裡（見 CommandRouter/Program.cs 與 Common/NatsMessagingRegistration.cs）。
	//
	// 需要先呼叫 AddRoomPackets()：sweeper 用同一個 RoomBroadcaster，而且它廣播的
	// RoomMemberLeft 要靠 registry 才反查得到 subject。
	public static IServiceCollection AddRoomMembershipMaintenance(this IServiceCollection services) =>
		services.AddHostedService<RoomGraceSweeper>();
}
