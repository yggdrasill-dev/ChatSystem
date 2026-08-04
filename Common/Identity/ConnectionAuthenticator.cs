using Common.Connections;
using Microsoft.Extensions.Logging;

namespace Common.Identity;

internal sealed class ConnectionAuthenticator(
	ISessionStore sessionStore,
	IPresenceDirectory presenceDirectory,
	IConnectionTerminator connectionTerminator,
	ILogger<ConnectionAuthenticator> logger) : IConnectionAuthenticator
{
	public async ValueTask<string?> ResolveAsync(string? sessionToken, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(sessionToken))
			return null;

		var userId = await sessionStore.ResolveUserIdAsync(sessionToken, cancellationToken).ConfigureAwait(false);

		if (userId is null)
			return null;

		// sliding 續期就掛在這裡：連線建立是低頻事件，多一次 Redis 寫入無所謂。前一版設計
		// （每則命令都會經過的守門 filter）沒有這種低頻的著力點。
		await sessionStore.RefreshAsync(sessionToken, cancellationToken).ConfigureAwait(false);

		return userId;
	}

	public async ValueTask BindConnectionAsync(
		string principal,
		string connectionId,
		CancellationToken cancellationToken = default)
	{
		var superseded = await presenceDirectory
			.BindAsync(principal, connectionId, cancellationToken)
			.ConfigureAwait(false);

		if (superseded is null)
			return;

		// 不記 principal：那是使用者身分，log 裡放 connectionId 就足以追這件事的因果。
		logger.LogInformation(
			"{ConnectionId} superseded {SupersededConnectionId}, terminating the older one.",
			connectionId,
			superseded);

		// 舊連線可能在別的 Gateway 節點上，所以走 Dispatcher 的 fan-out；如果它其實已經
		// 自己斷了，查不到節點就被省略，不會報錯。
		await connectionTerminator
			.TerminateAsync([superseded], cancellationToken)
			.ConfigureAwait(false);
	}

	public ValueTask UnbindConnectionAsync(
		string principal,
		string connectionId,
		CancellationToken cancellationToken = default) =>
		presenceDirectory.UnbindAsync(principal, connectionId, cancellationToken);
}
