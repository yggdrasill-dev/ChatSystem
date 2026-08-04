using Common.Connections;
using Common.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Common.Tests.Identity;

public class ConnectionAuthenticatorTests
{
	[Fact]
	public async Task ResolveAsync_ReturnsNull_WithoutTouchingRedis_WhenThereIsNoCookie()
	{
		var context = new AuthenticatorContext();

		Assert.Null(await context.Authenticator.ResolveAsync(null));

		// handshake 上沒有 cookie 是常態（還沒登入的人），不該為此打一次 Redis
		await context.Sessions.DidNotReceive().ResolveUserIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ResolveAsync_ReturnsNull_AndDoesNotRefresh_WhenTheSessionIsUnknown()
	{
		var context = new AuthenticatorContext();
		context.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>()).Returns((string?)null);

		Assert.Null(await context.Authenticator.ResolveAsync("token-1"));
		await context.Sessions.DidNotReceive().RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ResolveAsync_ReturnsThePrincipal_AndSlidesTheSession()
	{
		var context = new AuthenticatorContext();
		context.Sessions.ResolveUserIdAsync("token-1", Arg.Any<CancellationToken>()).Returns("user-1");

		Assert.Equal("user-1", await context.Authenticator.ResolveAsync("token-1"));

		// 續期掛在 handshake：連線建立是低頻事件，這是整個流程裡唯一適合的著力點
		await context.Sessions.Received(1).RefreshAsync("token-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task BindConnectionAsync_TerminatesTheConnectionThatWasSuperseded()
	{
		var context = new AuthenticatorContext();
		context.Presence
			.BindConnectionAsync("user-1", "conn-new", Arg.Any<CancellationToken>())
			.Returns("conn-old");

		await context.Authenticator.BindConnectionAsync("user-1", "conn-new");

		// Supersede：同一身分同時只有一條連線生效，被取代的那條由這裡踢掉
		await context.Terminator.Received(1).TerminateAsync(
			Arg.Is<IReadOnlyCollection<string>>(ids => ids != null && ids.SequenceEqual(new[] { "conn-old" })),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task BindConnectionAsync_TerminatesNothing_WhenTheIdentityHadNoConnection()
	{
		var context = new AuthenticatorContext();
		context.Presence
			.BindConnectionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns((string?)null);

		await context.Authenticator.BindConnectionAsync("user-1", "conn-new");

		await context.Terminator.DidNotReceive().TerminateAsync(
			Arg.Any<IReadOnlyCollection<string>>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UnbindConnectionAsync_PassesTheConnectionIdThrough_ForFencing()
	{
		var context = new AuthenticatorContext();

		await context.Authenticator.UnbindConnectionAsync("user-1", "conn-1");

		// connectionId 必須傳下去：Supersede 時新連線已經先綁好，舊連線的解綁不能把它蓋掉
		await context.Presence.Received(1).UnbindConnectionAsync("user-1", "conn-1", Arg.Any<CancellationToken>());
	}

	private sealed class AuthenticatorContext
	{
		public AuthenticatorContext() =>
			Authenticator = new ConnectionAuthenticator(
				Sessions,
				Presence,
				Terminator,
				NullLogger<ConnectionAuthenticator>.Instance);

		public ISessionStore Sessions { get; } = Substitute.For<ISessionStore>();

		public IPresenceDirectory Presence { get; } = Substitute.For<IPresenceDirectory>();

		public IConnectionTerminator Terminator { get; } = Substitute.For<IConnectionTerminator>();

		public IConnectionAuthenticator Authenticator { get; }
	}
}
