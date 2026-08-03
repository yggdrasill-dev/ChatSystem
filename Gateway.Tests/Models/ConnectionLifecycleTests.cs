using Common.Connections;
using Gateway.Models;
using Gateway.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Gateway.Tests.Models;

public class ConnectionLifecycleTests
{
	[Fact]
	public async Task OnConnectedAsync_AddsToRegistry_AndRegistersInDirectory()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var lifecycle = CreateLifecycle(registry, directory, Substitute.For<IConnectionEventPublisher>());

		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		Assert.Equal(1, registry.Count);
		await directory.Received(1).RegisterAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnConnectedAsync_DoesNotPublishAnyEvent()
	{
		var events = Substitute.For<IConnectionEventPublisher>();
		var lifecycle = CreateLifecycle(new ConnectionRegistry(), Substitute.For<IConnectionDirectory>(), events);

		await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		// 連線層目前只對外發「已斷線」一個事件；建立連線不發，因為沒有上層需要它
		await events.DidNotReceive().PublishDisconnectedAsync(
			Arg.Any<string>(),
			Arg.Any<string>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_RemovesFromRegistry_AndUnregistersFromDirectory()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var lifecycle = CreateLifecycle(registry, directory, Substitute.For<IConnectionEventPublisher>());
		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		await lifecycle.OnDisconnectedAsync(connection.ConnectionId);

		Assert.Equal(0, registry.Count);
		await directory.Received(1).UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_PublishesTheDisconnectedEvent_AfterCleaningUp()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var events = Substitute.For<IConnectionEventPublisher>();
		var lifecycle = CreateLifecycle(registry, directory, events);
		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		await lifecycle.OnDisconnectedAsync(connection.ConnectionId);

		await events.Received(1).PublishDisconnectedAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());

		// 順序有意義：訂閱端收到事件時，這條連線必須已經不在 ConnectionDirectory 上，
		// 否則會出現「收到斷線通知卻還查得到節點」的中間狀態。
		Received.InOrder(() =>
		{
			directory.UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
			events.PublishDisconnectedAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());
		});
	}

	[Fact]
	public async Task OnDisconnectedAsync_StillCleansUp_WhenPublishingTheEventFails()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var events = Substitute.For<IConnectionEventPublisher>();
		events
			.PublishDisconnectedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Throws(new InvalidOperationException("nats is down"));

		var lifecycle = CreateLifecycle(registry, directory, events);
		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		// 事件是 best-effort：發不出去不能讓清理失敗，也不能把例外丟給 receive loop 的 finally
		await lifecycle.OnDisconnectedAsync(connection.ConnectionId);

		Assert.Equal(0, registry.Count);
		await directory.Received(1).UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
	}

	private static ConnectionLifecycle CreateLifecycle(
		ConnectionRegistry registry,
		IConnectionDirectory directory,
		IConnectionEventPublisher events) =>
		new(new GatewayNodeId("node-1"), registry, directory, events, NullLogger<ConnectionLifecycle>.Instance);
}
