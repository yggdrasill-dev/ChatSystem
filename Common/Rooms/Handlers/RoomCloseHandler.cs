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

		// 名單要在刪除之前先讀：刪掉之後成員會被清掉，就不知道該通知誰了。
		var members = await membership.GetMembersAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		// TryDeleteAsync 回 false = 這期間房間已經被刪掉了（上面那次 GetAsync 之後）。兩個 close
		// 併發時只有一個刪得到，所以 RoomClosed 不會被廣播兩次——ADR-10 之前這裡是刻意接受的
		// 重複廣播。client 的 idempotent 要求還在，但理由換成 RoomMemberLeft 那個（6.5）。
		var deleted = await roomStore.TryDeleteAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ReplyAsync(
				context,
				RoomReply.Of(
					deleted ? RoomOperationReply.Types.Status.Ok : RoomOperationReply.Types.Status.RoomNotFound,
					message.RoomId),
				cancellationToken)
			.ConfigureAwait(false);

		if (!deleted)
			return;

		await broadcaster
			.ToUsersAsync(
				[.. members.Select(member => member.UserId)],
				new RoomClosed { RoomId = message.RoomId },
				cancellationToken)
			.ConfigureAwait(false);

		// 把成員清掉：房間已經不在了，留著只會讓 GetMembersAsync 一直回一群不該存在的人，
		// 而且他們的 userId → roomId 指向也該釋放。
		foreach (var member in members)
			await membership.RemoveAsync(message.RoomId, member.UserId, cancellationToken).ConfigureAwait(false);
	}
}
