using Adaptare;
using Chat.Protos;
using Common.Delivery;
using Google.Protobuf;
using NSubstitute;

namespace Common.Tests.Delivery;

public class OutboundGatewayTests
{
	[Fact]
	public async Task DeliverAsync_PublishesDeliverRequest_ToDispatchDeliverSubject()
	{
		var sender = Substitute.For<IMessageSender>();
		var gateway = new OutboundGateway(sender);

		var connectionIds = new[] { "conn-a", "conn-b" };
		await gateway.DeliverAsync("chat.receive", connectionIds, ByteString.CopyFromUtf8("hi"));

		await sender.Received(1).PublishAsync(
			"dispatch.deliver",
			Arg.Is<byte[]>(bytes => Matches(bytes, "chat.receive", connectionIds, "hi")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	private static bool Matches(byte[]? bytes, string subject, string[] connectionIds, string payloadText)
	{
		var request = DeliverRequest.Parser.ParseFrom(bytes);

		return request.Subject == subject
			&& request.ConnectionIds.SequenceEqual(connectionIds)
			&& request.Payload.ToStringUtf8() == payloadText;
	}
}
