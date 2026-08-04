using Common.Identity;
using Common.Protocol;
using Google.Protobuf;

namespace Common.Rooms;

// 房間層每個 handler 都要做的兩件事：回覆發問的那條連線、以及廣播給一群成員。
//
// 「決定名單」跟「負責投遞」是分開的兩層（connection-layer.md ADR-3）：這裡把 userId 換成
// connectionId，實際投遞交給協定層的 IPacketPublisher 再往下走 Dispatcher。
internal sealed class RoomBroadcaster(IPresenceDirectory presenceDirectory, IPacketPublisher packetPublisher)
{
	// 回覆直接送回發問的那條連線，不查 Presence：那條連線就在 context 裡，而且 Presence
	// 可能已經指向別的連線（Supersede），查了反而會回錯人。
	public ValueTask ReplyAsync<TMessage>(
		CommandContext context,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage> =>
		packetPublisher.PublishAsync([context.ConnectionId], message, cancellationToken);

	public async ValueTask ToUsersAsync<TMessage>(
		IReadOnlyCollection<string> userIds,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage>
	{
		if (userIds.Count == 0)
			return;

		// 不在線的 userId 直接被省略；寬限期中的成員因此自然不會收到（room-layer.md 5.2）。
		var connectionIds = await presenceDirectory
			.ResolveConnectionsAsync(userIds, cancellationToken)
			.ConfigureAwait(false);

		if (connectionIds.Count == 0)
			return;

		await packetPublisher.PublishAsync(connectionIds, message, cancellationToken).ConfigureAwait(false);
	}

	public ValueTask ToUserAsync<TMessage>(
		string userId,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage> =>
		ToUsersAsync([userId], message, cancellationToken);
}
