using StackExchange.Redis;

namespace Common.Rooms;

internal sealed class RedisRoomBanList(IConnectionMultiplexer multiplexer) : IRoomBanList
{
	public async ValueTask<bool> IsBannedAsync(string roomId, string userId, CancellationToken cancellationToken = default) =>
		await multiplexer.GetDatabase().SetContainsAsync(RoomKeys.Ban(roomId), userId).ConfigureAwait(false);

	public ValueTask BanAsync(string roomId, string userId, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().SetAddAsync(RoomKeys.Ban(roomId), userId));

	public ValueTask UnbanAsync(string roomId, string userId, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().SetRemoveAsync(RoomKeys.Ban(roomId), userId));
}
