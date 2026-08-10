using System.Collections.Concurrent;
using Common.Connections;
using Common.Identity;
using Common.Rooms;

namespace Integration.Tests;

// 這些替身只負責讓「層與層的組合」跑得起來，不是 Redis 實作的規格。Redis 的語意（Lua 的
// fencing、GETSET、讀取時過濾）由 Common.Tests 裡對著 mock IDatabase 的單元測試守。
//
// IRoomStore / IRoomBanList 的替身**不在這裡**，移到 Common.Tests.Rooms 了：那兩個同時是
// RoomStoreContractTests / RoomBanListContractTests 的跑道，兩邊各留一份會漂移。
//
// 但有兩件事刻意跟 Redis 版對齊，因為 handler 的正確性依賴它們：
//   1. GetMembersAsync 在讀取時就濾掉寬限期已過的成員（room-layer.md ADR-2）
//   2. 「userId → roomId」的指向只有在還指向這個房間時才會被清掉（fencing）
internal sealed class InMemoryRoomMembership(TimeProvider timeProvider) : IRoomMembership
{
	internal static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RoomMember>> m_Members = new();
	private readonly ConcurrentDictionary<string, string> m_UserRoom = new();

	public ValueTask<IReadOnlyCollection<RoomMember>> GetMembersAsync(
		string roomId,
		CancellationToken cancellationToken = default)
	{
		var cutoff = timeProvider.GetUtcNow() - GracePeriod;

		IReadOnlyCollection<RoomMember> members = m_Members.TryGetValue(roomId, out var byUser)
			? [.. byUser.Values.Where(member => member.DisconnectedAt is null || member.DisconnectedAt > cutoff)]
			: [];

		return ValueTask.FromResult(members);
	}

	public ValueTask<string?> GetCurrentRoomAsync(string userId, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(m_UserRoom.TryGetValue(userId, out var roomId) ? roomId : null);

	public ValueTask JoinAsync(
		string roomId,
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		m_Members.GetOrAdd(roomId, _ => new())[userId] =
			new RoomMember(userId, timeProvider.GetUtcNow(), connectionId, null);
		m_UserRoom[userId] = roomId;

		return ValueTask.CompletedTask;
	}

	public ValueTask RemoveAsync(string roomId, string userId, CancellationToken cancellationToken = default)
	{
		if (m_Members.TryGetValue(roomId, out var byUser))
			byUser.TryRemove(userId, out _);

		// fencing：使用者可能已經加入別的房間了，這時不能把新的指向蓋掉。
		if (m_UserRoom.TryGetValue(userId, out var current) && current == roomId)
			m_UserRoom.TryRemove(userId, out _);

		return ValueTask.CompletedTask;
	}

	public ValueTask MarkDisconnectedAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		if (!m_UserRoom.TryGetValue(userId, out var roomId)
			|| !m_Members.TryGetValue(roomId, out var byUser)
			|| !byUser.TryGetValue(userId, out var member)
			// ADR-4 的 fencing：只有目前那條連線斷掉才算。
			|| member.CurrentConnectionId != connectionId)
			return ValueTask.CompletedTask;

		byUser[userId] = member with { DisconnectedAt = timeProvider.GetUtcNow() };

		return ValueTask.CompletedTask;
	}

	public ValueTask<IReadOnlyCollection<(string RoomId, string UserId)>> ListExpiredAsync(
		CancellationToken cancellationToken = default)
	{
		var cutoff = timeProvider.GetUtcNow() - GracePeriod;

		IReadOnlyCollection<(string, string)> expired =
		[
			.. m_Members.SelectMany(room => room.Value.Values
				.Where(member => member.DisconnectedAt is not null && member.DisconnectedAt <= cutoff)
				.Select(member => (room.Key, member.UserId)))
		];

		return ValueTask.FromResult(expired);
	}
}

internal sealed class InMemoryPresenceDirectory : IPresenceDirectory
{
	private readonly ConcurrentDictionary<string, string> m_Presence = new();

	public ValueTask<string?> BindConnectionAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		// SET ... GET 的語意：回傳被取代掉的舊值。
		var previous = m_Presence.TryGetValue(userId, out var existing) ? existing : null;
		m_Presence[userId] = connectionId;

		return ValueTask.FromResult(previous);
	}

	public ValueTask UnbindConnectionAsync(
		string userId,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		if (m_Presence.TryGetValue(userId, out var current) && current == connectionId)
			m_Presence.TryRemove(userId, out _);

		return ValueTask.CompletedTask;
	}

	public ValueTask<IReadOnlyCollection<string>> ResolveConnectionsAsync(
		IReadOnlyCollection<string> userIds,
		CancellationToken cancellationToken = default) =>
		// 查不到的（不在線）直接省略，跟 Redis 版一樣。
		ValueTask.FromResult<IReadOnlyCollection<string>>(
			[.. userIds.Select(userId => m_Presence.TryGetValue(userId, out var id) ? id : null).OfType<string>()]);
}

internal sealed class InMemoryConnectionDirectory : IConnectionDirectory
{
	private readonly ConcurrentDictionary<string, string> m_Nodes = new();

	public ValueTask RegisterAsync(string connectionId, string nodeId, CancellationToken cancellationToken = default)
	{
		m_Nodes[connectionId] = nodeId;

		return ValueTask.CompletedTask;
	}

	public ValueTask UnregisterAsync(string connectionId, CancellationToken cancellationToken = default)
	{
		m_Nodes.TryRemove(connectionId, out _);

		return ValueTask.CompletedTask;
	}

	public ValueTask<IReadOnlyDictionary<string, string>> ResolveNodesAsync(
		IReadOnlyCollection<string> connectionIds,
		CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
			connectionIds
				.Where(m_Nodes.ContainsKey)
				.ToDictionary(connectionId => connectionId, connectionId => m_Nodes[connectionId]));
}
