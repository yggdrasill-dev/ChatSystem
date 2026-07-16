using Common.Connections;
using Gateway.Models;
using Gateway.Tests.TestSupport;
using NSubstitute;

namespace Gateway.Tests.Models;

public class ConnectionLifecycleTests
{
	[Fact]
	public async Task OnConnectedAsync_AddsToRegistry_AndRegistersInDirectory()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var lifecycle = new ConnectionLifecycle(new GatewayNodeId("node-1"), registry, directory);

		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		Assert.Equal(1, registry.Count);
		await directory.Received(1).RegisterAsync(connection.ConnectionId, "node-1", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task OnDisconnectedAsync_RemovesFromRegistry_AndUnregistersFromDirectory()
	{
		var registry = new ConnectionRegistry();
		var directory = Substitute.For<IConnectionDirectory>();
		var lifecycle = new ConnectionLifecycle(new GatewayNodeId("node-1"), registry, directory);
		var connection = await lifecycle.OnConnectedAsync(new RecordingFakeWebSocket());

		await lifecycle.OnDisconnectedAsync(connection.ConnectionId);

		Assert.Equal(0, registry.Count);
		await directory.Received(1).UnregisterAsync(connection.ConnectionId, Arg.Any<CancellationToken>());
	}
}
