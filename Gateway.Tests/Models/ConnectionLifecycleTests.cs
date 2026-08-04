using Common.Connections;
using Common.Identity;
using Gateway.Models;
using Gateway.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Gateway.Tests.Models;

public class ConnectionLifecycleTests
{
	private const string Principal = "user-1";

	[Fact]
	public async Task OnConnectedAsync_AddsToRegistry_AndRegistersInDirectory()
	{
		var context = new LifecycleContext();

		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		Assert.Equal(1, context.Registry.Count);
		await context.Directory.Received(1).RegisterAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnConnectedAsync_CarriesThePrincipalOnTheConnection()
	{
		var context = new LifecycleContext();

		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		// receive loop 每則訊息都從這裡取 principal，而不是重查一次 Redis
		Assert.Equal(Principal, connection.Principal);
	}

	[Fact]
	public async Task OnConnectedAsync_BindsThePrincipal_AfterRegisteringInTheDirectory()
	{
		var context = new LifecycleContext();

		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		// 綁定只能在 ConnectionId 產生之後，所以「已 accept、尚未綁定」有一個小窗口——
		// 這個順序是刻意的，不是可以隨手調換的細節
		Received.InOrder(() =>
		{
			context.Directory.RegisterAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());
			context.Authenticator.BindConnectionAsync(Principal, connection.ConnectionId, Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task OnConnectedAsync_DoesNotPublishAnyEvent()
	{
		var context = new LifecycleContext();

		await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		// 連線層目前只對外發「已斷線」一個事件；建立連線不發，因為沒有上層需要它
		await context.Events.DidNotReceive().PublishDisconnectedAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_RemovesFromRegistry_AndUnregistersFromDirectory()
	{
		var context = new LifecycleContext();
		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		await context.Lifecycle.OnDisconnectedAsync(connection.ConnectionId, connection.Principal);

		Assert.Equal(0, context.Registry.Count);
		await context.Directory.Received(1).UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_UnbindsWithTheConnectionId()
	{
		var context = new LifecycleContext();
		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		await context.Lifecycle.OnDisconnectedAsync(connection.ConnectionId, connection.Principal);

		// connectionId 要一起傳下去，身分層才能 fencing：Supersede 時新連線已經先綁好了，
		// 這裡不能把它蓋掉
		await context.Authenticator.Received(1).UnbindConnectionAsync(
			Principal,
			connection.ConnectionId,
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_PublishesTheEventWithThePrincipal_AfterCleaningUp()
	{
		var context = new LifecycleContext();
		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		await context.Lifecycle.OnDisconnectedAsync(connection.ConnectionId, connection.Principal);

		// 事件帶 principal，訂閱端（房間層）因此不需要 connectionId -> 身分的反向索引
		await context.Events.Received(1).PublishDisconnectedAsync(
			connection.ConnectionId,
			"node-1",
			Principal,
			Arg.Any<CancellationToken>());

		// 順序有意義：訂閱端收到事件時，這條連線必須已經不在 ConnectionDirectory 上，
		// 否則會出現「收到斷線通知卻還查得到節點」的中間狀態。
		Received.InOrder(() =>
		{
			context.Directory.UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
			context.Events.PublishDisconnectedAsync(connection.ConnectionId, "node-1", Principal, Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task OnDisconnectedAsync_StillCleansUp_WhenPublishingTheEventFails()
	{
		var context = new LifecycleContext();
		context.Events
			.PublishDisconnectedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new InvalidOperationException("nats is down"));

		var connection = await context.Lifecycle.OnConnectedAsync(new RecordingFakeWebSocket(), Principal);

		// 事件是 best-effort：發不出去不能讓清理失敗，也不能把例外丟給 receive loop 的 finally
		await context.Lifecycle.OnDisconnectedAsync(connection.ConnectionId, connection.Principal);

		Assert.Equal(0, context.Registry.Count);
		await context.Directory.Received(1).UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
		await context.Authenticator.Received(1).UnbindConnectionAsync(Principal, connection.ConnectionId, Arg.Any<CancellationToken>());
	}

	private sealed class LifecycleContext
	{
		public LifecycleContext() =>
			Lifecycle = new(
				new GatewayNodeId("node-1"),
				Registry,
				Directory,
				Events,
				Authenticator,
				NullLogger<ConnectionLifecycle>.Instance);

		public ConnectionRegistry Registry { get; } = new();

		public IConnectionDirectory Directory { get; } = Substitute.For<IConnectionDirectory>();

		public IConnectionEventPublisher Events { get; } = Substitute.For<IConnectionEventPublisher>();

		public IConnectionAuthenticator Authenticator { get; } = Substitute.For<IConnectionAuthenticator>();

		public ConnectionLifecycle Lifecycle { get; }
	}
}
