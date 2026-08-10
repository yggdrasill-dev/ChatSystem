using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomJoinHandler(
	IRoomStore roomStore,
	IRoomBanList banList,
	IRoomMembership membership,
	RoomBroadcaster broadcaster) : IPacketHandler<JoinRoomRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		JoinRoomRequest message,
		CancellationToken cancellationToken = default)
	{
		var room = await roomStore.GetAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		if (room is null)
		{
			await ReplyAsync(context, RoomOperationReply.Types.Status.RoomNotFound, message.RoomId, cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		if (await banList.IsBannedAsync(message.RoomId, context.Principal, cancellationToken).ConfigureAwait(false))
		{
			await ReplyAsync(context, RoomOperationReply.Types.Status.Banned, message.RoomId, cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		if (!RoomPassword.Verify(message.Password, room.PasswordHash))
		{
			await ReplyAsync(context, RoomOperationReply.Types.Status.WrongPassword, message.RoomId, cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		var membersBefore = await membership.GetMembersAsync(message.RoomId, cancellationToken).ConfigureAwait(false);

		// 已經在名單上就是「重連」（可能還在寬限期中）。這種情況**不能**廣播 RoomMemberJoined，
		// 否則其他成員會看到一次無意義的加入事件——ADR-2 承諾的是「其他成員什麼都看不到」。
		var isReconnect = membersBefore.Any(member => member.UserId == context.Principal);

		await LeaveCurrentRoomAsync(context, message.RoomId, cancellationToken).ConfigureAwait(false);
		await membership
			.JoinAsync(message.RoomId, context.Principal, context.ConnectionId, cancellationToken)
			.ConfigureAwait(false);

		// JoinAsync 只動這一個成員，所以最終名單就是「原本的名單 ∪ 自己」，不必再讀一次。
		var memberIds = membersBefore
			.Select(member => member.UserId)
			.Append(context.Principal)
			.Distinct()
			.ToArray();

		var joined = new RoomJoined { RoomId = message.RoomId };
		joined.MemberUserIds.AddRange(memberIds);

		await broadcaster.ReplyAsync(context, joined, cancellationToken).ConfigureAwait(false);

		if (isReconnect)
			return;

		await broadcaster
			.ToUsersAsync(
				[.. memberIds.Where(userId => userId != context.Principal)],
				new RoomMemberJoined { RoomId = message.RoomId, UserId = context.Principal },
				cancellationToken)
			.ConfigureAwait(false);
	}

	// 「一個使用者一次只能在一間房」：加入新房間會自動離開舊的，而且**舊房間的成員要被通知**。
	// 少了這一步，切換房間時舊房間的 client 名單上會留一個永遠不會消失的幽靈——sweeper 只
	// 處理寬限期到期，明確的離開不走那條路。
	private async ValueTask LeaveCurrentRoomAsync(
		CommandContext context,
		string joiningRoomId,
		CancellationToken cancellationToken)
	{
		var current = await membership.GetCurrentRoomAsync(context.Principal, cancellationToken).ConfigureAwait(false);

		if (current is null || current == joiningRoomId)
			return;

		await membership.RemoveAsync(current, context.Principal, cancellationToken).ConfigureAwait(false);

		var remaining = await membership.GetMembersAsync(current, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ToUsersAsync(
				[.. remaining.Select(member => member.UserId)],
				new RoomMemberLeft { RoomId = current, UserId = context.Principal },
				cancellationToken)
			.ConfigureAwait(false);
	}

	private ValueTask ReplyAsync(
		CommandContext context,
		RoomOperationReply.Types.Status status,
		string roomId,
		CancellationToken cancellationToken) =>
		broadcaster.ReplyAsync(context, RoomReply.Of(status, roomId), cancellationToken);
}
