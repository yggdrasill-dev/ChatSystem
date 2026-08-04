using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomLeaveHandler(
	IRoomMembership membership,
	RoomBroadcaster broadcaster) : IPacketHandler<LeaveRoomRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		LeaveRoomRequest message,
		CancellationToken cancellationToken = default)
	{
		var current = await membership.GetCurrentRoomAsync(context.Principal, cancellationToken).ConfigureAwait(false);

		// 刻意檢查「你真的在這間房嗎」而不是無條件 Remove：無條件的話，client 傳錯 roomId
		// 會靜默成功，而且我們會對一間他不在的房間廣播他離開。
		if (current != message.RoomId)
		{
			await broadcaster
				.ReplyAsync(
					context,
					RoomReply.Of(RoomOperationReply.Types.Status.NotAMember, message.RoomId),
					cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		await membership.RemoveAsync(message.RoomId, context.Principal, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ReplyAsync(context, RoomReply.Of(RoomOperationReply.Types.Status.Ok, message.RoomId), cancellationToken)
			.ConfigureAwait(false);

		var remaining = await membership.GetMembersAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ToUsersAsync(
				[.. remaining.Select(member => member.UserId)],
				new RoomMemberLeft { RoomId = message.RoomId, UserId = context.Principal },
				cancellationToken)
			.ConfigureAwait(false);
	}
}
