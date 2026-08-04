using StackExchange.Redis;

namespace Common.Identity;

internal sealed class RedisSessionStore(IConnectionMultiplexer multiplexer) : ISessionStore
{
	// 7 天，隨活動 sliding 續期。跟連線層那個 30 秒 TTL 不是同一種東西：那個續的是
	// 「這條連線還活著」，這個續的是「這個人還登入著」。
	private static readonly TimeSpan _Ttl = TimeSpan.FromDays(7);

	public async ValueTask<string> CreateSessionAsync(string userId, CancellationToken cancellationToken = default)
	{
		var sessionToken = OpaqueToken.New();

		await multiplexer.GetDatabase()
			.StringSetAsync(IdentityKeys.Session(sessionToken), userId, _Ttl)
			.ConfigureAwait(false);

		return sessionToken;
	}

	public async ValueTask<string?> ResolveUserIdAsync(string sessionToken, CancellationToken cancellationToken = default)
	{
		// 呼叫端是「cookie 可能根本不存在」的 handshake 路徑，所以空字串當作沒有 session，
		// 不去查一個必定不存在的 key。
		if (string.IsNullOrEmpty(sessionToken))
			return null;

		var userId = await multiplexer.GetDatabase()
			.StringGetAsync(IdentityKeys.Session(sessionToken))
			.ConfigureAwait(false);

		return userId.IsNullOrEmpty ? null : (string?)userId;
	}

	// key 已經不存在時 KeyExpire 回 false，這裡不理它：續期失敗的唯一後果是這個 session
	// 照原本的到期時間過期，而呼叫端已經在 ResolveUserIdAsync 那步知道它存不存在了。
	public ValueTask RefreshAsync(string sessionToken, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().KeyExpireAsync(IdentityKeys.Session(sessionToken), _Ttl));

	public ValueTask RevokeAsync(string sessionToken, CancellationToken cancellationToken = default) =>
		new(multiplexer.GetDatabase().KeyDeleteAsync(IdentityKeys.Session(sessionToken)));
}
