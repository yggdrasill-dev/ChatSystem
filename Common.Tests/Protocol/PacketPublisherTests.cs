using Chat.Protos;
using Common.Delivery;
using Common.Protocol;
using Google.Protobuf;
using NSubstitute;

namespace Common.Tests.Protocol;

public class PacketPublisherTests
{
	[Fact]
	public async Task PublishAsync_ResolvesSubjectFromRegistry_AndDelegatesToOutboundGateway()
	{
		var registry = new PacketRegistry([new PacketRegistration("chat.receive", typeof(Packet), null)]);
		var outboundGateway = Substitute.For<IOutboundGateway>();
		var publisher = new PacketPublisher(registry, outboundGateway);

		var message = new Packet { Subject = "inner", Payload = ByteString.CopyFromUtf8("hi") };

		await publisher.PublishAsync(["conn-a", "conn-b"], message);

		// 呼叫端從來沒寫過 "chat.receive"，subject 是 registry 依型別反查出來的
		await outboundGateway.Received(1).DeliverAsync(
			"chat.receive",
			Arg.Is<IReadOnlyCollection<string>>(ids => ids != null && ids.SequenceEqual(new[] { "conn-a", "conn-b" })),
			message.ToByteString(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task PublishAsync_Throws_WhenTheMessageTypeHasNoRegisteredSubject()
	{
		var registry = new PacketRegistry([]);
		var publisher = new PacketPublisher(registry, Substitute.For<IOutboundGateway>());

		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await publisher.PublishAsync(["conn-a"], new Packet()));
	}
}
