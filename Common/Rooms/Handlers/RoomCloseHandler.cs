using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomCloseHandler(
	IRoomStore roomStore,
	IRoomMembership membership,
	RoomBroadcaster broadcaster) : IPacketHandler<CloseRoomRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		CloseRoomRequest message,
		CancellationToken cancellationToken = default)
	{
		var room = await roomStore.GetAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		if (room is null || room.OwnerUserId != context.Principal)
		{
			await broadcaster
				.ReplyAsync(
					context,
					RoomReply.Of(
						room is null
							? RoomOperationReply.Types.Status.RoomNotFound
							: RoomOperationReply.Types.Status.NotOwner,
						message.RoomId),
					cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		// 名單要在關閉之前先讀：關閉之後成員會被清掉，就不知道該通知誰了。
		var members = await membership.GetMembersAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		// TryCloseAsync 回 false = 已經是關閉狀態。兩個 close 併發時兩邊都可能回 true 導致
		// RoomClosed 廣播兩次，client 要對重複的關閉通知照 idempotent 處理（6.1 刻意接受）。
		var closed = await roomStore.TryCloseAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ReplyAsync(
				context,
				RoomReply.Of(
					closed ? RoomOperationReply.Types.Status.Ok : RoomOperationReply.Types.Status.RoomClosed,
					message.RoomId),
				cancellationToken)
			.ConfigureAwait(false);

		if (!closed)
			return;

		await broadcaster
			.ToUsersAsync(
				[.. members.Select(member => member.UserId)],
				new RoomClosed { RoomId = message.RoomId },
				cancellationToken)
			.ConfigureAwait(false);

		// 把成員清掉：房間已關，留著只會讓 GetMembersAsync 一直回一群不該存在的人，
		// 而且他們的 userId → roomId 指向也該釋放。
		foreach (var member in members)
			await membership.RemoveAsync(message.RoomId, member.UserId, cancellationToken).ConfigureAwait(false);
	}
}
