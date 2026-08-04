using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomUpdateHandler(
	IRoomStore roomStore,
	RoomBroadcaster broadcaster) : IPacketHandler<UpdateRoomRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		UpdateRoomRequest message,
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

		// 三種意圖要能分開表達：清掉密碼（變公開房）、換新密碼、不動密碼。
		// 只用「password 是不是空字串」表達不了第一種，所以有 clear_password 這個欄位。
		var passwordHash = message switch
		{
			{ ClearPassword: true } => null,
			{ Password.Length: > 0 } => RoomPassword.Hash(message.Password),
			_ => room.PasswordHash,
		};

		var updated = await roomStore
			.TryUpdateSettingsAsync(message.RoomId, message.Name, passwordHash, cancellationToken)
			.ConfigureAwait(false);

		// 回 false 只可能是「這期間房間被關掉了」——併發 update 與 close 的競爭是 6.1 刻意
		// 接受的，已關閉的房間設定沒有意義。
		await broadcaster
			.ReplyAsync(
				context,
				RoomReply.Of(
					updated ? RoomOperationReply.Types.Status.Ok : RoomOperationReply.Types.Status.RoomClosed,
					message.RoomId),
				cancellationToken)
			.ConfigureAwait(false);
	}
}
