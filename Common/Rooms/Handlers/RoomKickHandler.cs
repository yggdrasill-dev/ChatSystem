using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomKickHandler(
	IRoomStore roomStore,
	IRoomMembership membership,
	RoomBroadcaster broadcaster) : IPacketHandler<KickMemberRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		KickMemberRequest message,
		CancellationToken cancellationToken = default)
	{
		var room = await roomStore.GetAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		// 「先讀房間檢查是不是房主、再動作」看起來像 TOCTOU，但 OwnerUserId 不可變更所以安全。
		// 以後若要加「轉移房主」，這四個後台流程都要重新檢視（見 Room 的註解）。
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

		await membership.RemoveAsync(message.RoomId, message.TargetUserId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ReplyAsync(context, RoomReply.Of(RoomOperationReply.Types.Status.Ok, message.RoomId), cancellationToken)
			.ConfigureAwait(false);

		// 被踢的人收到 RoomKicked，其他成員收到 RoomMemberLeft。
		// 刻意不呼叫 IConnectionTerminator——踢出房間不等於關閉連線，被踢的人應該回到房間列表
		// 而不是被斷線（ADR-5）。
		await broadcaster
			.ToUserAsync(message.TargetUserId, new RoomKicked { RoomId = message.RoomId }, cancellationToken)
			.ConfigureAwait(false);

		var remaining = await membership.GetMembersAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ToUsersAsync(
				[.. remaining.Select(member => member.UserId)],
				new RoomMemberLeft { RoomId = message.RoomId, UserId = message.TargetUserId },
				cancellationToken)
			.ConfigureAwait(false);
	}
}
