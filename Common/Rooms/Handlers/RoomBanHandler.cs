using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomBanHandler(
	IRoomStore roomStore,
	IRoomBanList banList,
	RoomBroadcaster broadcaster) : IPacketHandler<BanMemberRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		BanMemberRequest message,
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

		// 封鎖**不會**順手把人踢出房間：ADR-5 的立場是「後台的正常用法是踢出＋封鎖，UI 上把
		// 兩者做成一個動作」。合併進來的話「封鎖」這個命令就有兩種副作用，而且沒有辦法只封鎖
		// 一個不在房間裡的人（那是最常見的用法）。這個取捨值得再確認，見 room-layer.md 第 9 節。
		if (message.Unban)
			await banList.UnbanAsync(message.RoomId, message.TargetUserId, cancellationToken).ConfigureAwait(false);
		else
			await banList.BanAsync(message.RoomId, message.TargetUserId, cancellationToken).ConfigureAwait(false);

		await broadcaster
			.ReplyAsync(context, RoomReply.Of(RoomOperationReply.Types.Status.Ok, message.RoomId), cancellationToken)
			.ConfigureAwait(false);
	}
}
