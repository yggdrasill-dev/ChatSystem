using Chat.Protos;
using Common.Rooms;
using Common.Rooms.Handlers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

public static class RoomLayerServiceCollectionExtensions
{
	// 房間層用自己的 Redis 資源（AppHost 的 room-store），不共用連線層的 connection-directory：
	// 房間資料是持久資料、需要開 AOF/RDB，跟連線層那個純快取用途的設定需求不同，而且
	// connection-layer.md ADR-2 的擁有權原則就是「每一層的基礎設施由自己擁有」。
	//
	// 因為同一個 process（CommandRouter）會同時需要兩個 Redis，這裡必須用 keyed service
	// 取得對應的 client：先呼叫 builder.AddKeyedRedisClient(redisServiceKey)。
	public static IServiceCollection AddRoomStore(this IServiceCollection services, object redisServiceKey)
	{
		services.AddSingleton<IRoomStore>(sp =>
			new RedisRoomStore(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		services.AddSingleton<IRoomBanList>(sp =>
			new RedisRoomBanList(sp.GetRequiredKeyedService<IConnectionMultiplexer>(redisServiceKey)));

		// 成員名單是暫時狀態、不是持久資料，但仍然放房間層自己的 Redis：它跟房間資料要
		// 跨 key 一起操作（斷線標記那段 Lua），分開兩個 instance 就辦不到。
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
		services.AddScoped<RoomBroadcaster>();

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
}
