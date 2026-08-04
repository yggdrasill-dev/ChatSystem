using Chat.Protos;
using Common.Protocol;

namespace Common.Rooms.Handlers;

internal sealed class RoomListHandler(
	IRoomStore roomStore,
	IRoomMembership membership,
	RoomBroadcaster broadcaster) : IPacketHandler<ListRoomsRequest>
{
	public async ValueTask HandleAsync(
		CommandContext context,
		ListRoomsRequest message,
		CancellationToken cancellationToken = default)
	{
		var rooms = await roomStore.ListOpenAsync(cancellationToken).ConfigureAwait(false);
		var list = new RoomList();

		foreach (var room in rooms)
		{
			// 人數用 GetMembersAsync().Count 而不是直接數 hash 的欄位數：後者會把寬限期已過、
			// 還沒被 sweeper 清掉的幽靈成員一起算進去。代價是這裡在 ListOpenAsync 的 N+1
			// 之上又多一輪 N（room-layer.md §8 把分頁列為「先不做」時心裡有數的成本又長了一點）。
			var members = await membership.GetMembersAsync(room.RoomId, cancellationToken).ConfigureAwait(false);

			list.Rooms.Add(new RoomSummary
			{
				RoomId = room.RoomId,
				Name = room.Name,
				// 只揭露有沒有密碼（ADR-6）。
				HasPassword = room.PasswordHash is not null,
				MemberCount = members.Count,
			});
		}

		await broadcaster.ReplyAsync(context, list, cancellationToken).ConfigureAwait(false);
	}
}
