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

	[Fact]
	public async Task DeliverAsync_SplitsIntoMultiplePublishes_WhenBatchWouldExceedSizeBudget()
	{
		var sender = Substitute.For<IMessageSender>();
		var gateway = new OutboundGateway(sender);

		// payload 刻意調大，讓 DeliveryBatching 算出的批次上限縮小到方便測試的數量
		var payload = ByteString.CopyFrom(new byte[880_000]);
		var connectionIds = Enumerable.Range(0, 1_200).Select(i => $"conn-{i}").ToArray();

		var publishedBatches = new List<string[]>();
		sender
			.PublishAsync(
				Arg.Any<string>(),
				Arg.Do<byte[]>(bytes => publishedBatches.Add(DeliverRequest.Parser.ParseFrom(bytes).ConnectionIds.ToArray())),
				Arg.Any<IEnumerable<MessageHeaderValue>>(),
				Arg.Any<CancellationToken>())
			.Returns(ValueTask.CompletedTask);

		await gateway.DeliverAsync("chat.receive", connectionIds, payload);

		Assert.True(publishedBatches.Count > 1, "Expected the large connection id batch to be split across multiple publishes.");
		Assert.Equal(connectionIds, publishedBatches.SelectMany(b => b));
	}
}
