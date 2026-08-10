using System.Collections.Concurrent;
using Common.Rooms;

namespace Common.Tests.Rooms;

// IRoomStore / IRoomBanList 的 in-memory 實作。跟 Common/Chat/InMemoryChatMessageStore.cs 的
// 地位不同——那個是階段 A 真的註冊進 DI 的實作，這兩個只活在測試裡，正式路徑一直都是 Redis。
//
// 它們同時服務兩個地方：RoomStoreContractTests / RoomBanListContractTests 拿它們當契約的跑道，
// Integration.Tests 拿它們讓層與層的組合跑得起來（原本住在那裡，B0 移過來）。**只留一份**是
// 刻意的：兩邊各留一份的話，契約改了而組合測試那份沒跟上，症狀會是「單元全綠、整合莫名其妙」。
public sealed class InMemoryRoomStore : IRoomStore
{
	private readonly ConcurrentDictionary<string, Room> m_Rooms = new();

	public ValueTask<Room?> GetAsync(string roomId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_Rooms.TryGetValue(roomId, out var room) ? room : null);

	public ValueTask<IReadOnlyCollection<Room>> ListAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyCollection<Room>>([.. m_Rooms.Values]);

	public ValueTask<bool> TryCreateAsync(Room room, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_Rooms.TryAdd(room.RoomId, room));

	public ValueTask<bool> TryUpdateSettingsAsync(
		string roomId,
		string name,
		string? passwordHash,
		CancellationToken cancellationToken = default)
	{
		if (!m_Rooms.TryGetValue(roomId, out var room))
			return ValueTask.FromResult(false);

		m_Rooms[roomId] = room with { Name = name, PasswordHash = passwordHash };

		return ValueTask.FromResult(true);
	}

	// 封鎖名單不在這裡帶走：這兩個替身是各自獨立的物件，湊不出 Postgres 的 ON DELETE CASCADE。
	// **那條保證因此不在契約測試裡**，由 E2E.Tests 的
	// PostgresRoomBanListContractTests.Delete_TakesTheBanListWithIt_ThroughTheForeignKey 守。
	public ValueTask<bool> TryDeleteAsync(string roomId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_Rooms.TryRemove(roomId, out _));
}

public sealed class InMemoryRoomBanList : IRoomBanList
{
	private readonly ConcurrentDictionary<(string RoomId, string UserId), bool> m_Banned = new();

	public ValueTask<bool> IsBannedAsync(string roomId, string userId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_Banned.ContainsKey((roomId, userId)));

	public ValueTask BanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		m_Banned[(roomId, userId)] = true;

		return ValueTask.CompletedTask;
	}

	public ValueTask UnbanAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		m_Banned.TryRemove((roomId, userId), out _);

		return ValueTask.CompletedTask;
	}
}
