using StackExchange.Redis;

namespace Common.Identity;

internal sealed class RedisLoginNonceStore(IConnectionMultiplexer multiplexer) : ILoginNonceStore
{
	// 只需要撐過「使用者點下 Google 登入按鈕到 ID Token 回來」這段。
	private static readonly TimeSpan _Ttl = TimeSpan.FromMinutes(2);

	private const string Placeholder = "1";

	public async ValueTask<string> IssueAsync(CancellationToken cancellationToken = default)
	{
		var nonce = OpaqueToken.New();

		// 值本身沒有意義，存在與否才是資訊。
		await multiplexer.GetDatabase()
			.StringSetAsync(IdentityKeys.LoginNonce(nonce), Placeholder, _Ttl)
			.ConfigureAwait(false);

		return nonce;
	}

	public async ValueTask<bool> TryConsumeAsync(string nonce, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(nonce))
			return false;

		// DEL 的回傳值就是一次性守門：只有真的刪掉那一次算消耗成功，重放拿到 false。
		// 「先 EXISTS 再 DEL」會留下兩個請求都通過的空隙，那樣的 nonce 等於沒防。
		// 同一個手法也用在 RedisRoomStore.TryCreateAsync 的 SADD。
		return await multiplexer.GetDatabase()
			.KeyDeleteAsync(IdentityKeys.LoginNonce(nonce))
			.ConfigureAwait(false);
	}
}
