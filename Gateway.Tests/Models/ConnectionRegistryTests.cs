using Chat.Protos;
using Gateway.Models;
using Gateway.Tests.TestSupport;
using Google.Protobuf;

namespace Gateway.Tests.Models;

public class ConnectionRegistryTests
{
	[Fact]
	public async Task TryDeliverAsync_ReturnsFalse_WhenConnectionIdNotFound()
	{
		var registry = new ConnectionRegistry();

		var delivered = await registry.TryDeliverAsync("missing", "subject", ByteString.Empty);

		Assert.False(delivered);
	}

	[Fact]
	public async Task TryDeliverAsync_ReturnsTrue_AndSendsPacket_WhenConnectionExists()
	{
		var socket = new RecordingFakeWebSocket();
		var connection = new Connection("conn-1", socket);
		var registry = new ConnectionRegistry();
		registry.Add(connection);

		var delivered = await registry.TryDeliverAsync("conn-1", "chat.receive", ByteString.CopyFromUtf8("hi"));

		Assert.True(delivered);
		var packet = Packet.Parser.ParseFrom(Assert.Single(socket.SentFrames));
		Assert.Equal("chat.receive", packet.Subject);
		Assert.Equal("hi", packet.Payload.ToStringUtf8());
	}

	[Fact]
	public async Task Remove_MakesConnectionNoLongerDeliverable()
	{
		var registry = new ConnectionRegistry();
		registry.Add(new Connection("conn-1", new RecordingFakeWebSocket()));

		registry.Remove("conn-1");
		var delivered = await registry.TryDeliverAsync("conn-1", "subject", ByteString.Empty);

		Assert.False(delivered);
	}

	[Fact]
	public void ConnectionIds_ReflectsCurrentlyRegisteredConnections()
	{
		var registry = new ConnectionRegistry();
		registry.Add(new Connection("a", new RecordingFakeWebSocket()));
		registry.Add(new Connection("b", new RecordingFakeWebSocket()));

		Assert.Equal(["a", "b"], registry.ConnectionIds.OrderBy(id => id));
		Assert.Equal(2, registry.Count);
	}
}
