using StackExchange.Redis;

namespace Common.Identity;

internal sealed class RedisUserProfileStore(IConnectionMultiplexer multiplexer) : IUserProfileStore
{
	// 不設 TTL：這是持久資料，而且每次登入都會覆寫。
	public ValueTask SaveAsync(
		string userId,
		string? displayName,
		string? pictureUrl,
		CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().HashSetAsync(
			IdentityKeys.Profile(userId),
			[
				// 空字串代表「Google 沒給」，跟 RedisRoomStore 對 password_hash 的處理一致。
				new HashEntry(IdentityKeys.ProfileFields.DisplayName, displayName ?? string.Empty),
				new HashEntry(IdentityKeys.ProfileFields.PictureUrl, pictureUrl ?? string.Empty),
			]));
}
