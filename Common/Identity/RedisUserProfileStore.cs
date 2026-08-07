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

	// 單一 HGET，不是 HGETALL：聊天層只要 display_name，而這是在 chat.send 的熱路徑上
	// （每則訊息一次，chat-layer.md ADR-3 明說這次往返不優化）。
	public async ValueTask<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default)
	{
		var displayName = await multiplexer
			.GetDatabase()
			.HashGetAsync(IdentityKeys.Profile(userId), IdentityKeys.ProfileFields.DisplayName)
			.ConfigureAwait(false);

		// key 或 field 不存在都是 null；存在但空字串代表「Google 沒給」，那要照原樣回。
		return displayName.IsNull ? null : displayName.ToString();
	}
}
