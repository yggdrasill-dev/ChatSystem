using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomCreateHandler(
	IRoomStore roomStore,
	RoomBroadcaster broadcaster,
	TimeProvider timeProvider) : IPacketHandler<CreateRoomRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		CreateRoomRequest message,
		CancellationToken cancellationToken = default)
	{
		var room = new Room(
			Guid.NewGuid().ToString("N"),
			message.Name,
			string.IsNullOrEmpty(message.Password) ? null : RoomPassword.Hash(message.Password),
			context.Principal,
			timeProvider.GetUtcNow(),
			false);

		// roomId 是每次新產生的 Guid，撞到既有 id 不是業務失敗而是內部錯誤，所以往上丟讓
		// ack 帶 HANDLER_FAILED——ADR-8 要求「業務失敗必須回下行訊息」，這不是業務失敗。
		if (!await roomStore.TryCreateAsync(room, cancellationToken).ConfigureAwait(false))
			throw new InvalidOperationException($"Room id '{room.RoomId}' was already taken.");

		// 建房刻意不順便加入：join 有自己的一整套流程（退舊房、廣播、回名單），複製一份只會
		// 讓兩邊行為漂移。client 建完房之後自己送 room.join。
		await broadcaster
			.ReplyAsync(context, RoomReply.Of(RoomOperationReply.Types.Status.Ok, room.RoomId), cancellationToken)
			.ConfigureAwait(false);
	}
}
