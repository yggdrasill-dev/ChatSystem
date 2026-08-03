using Adaptare;
using Chat.Protos;
using Common.Connections;
using NSubstitute;

namespace Common.Tests.Connections;

public class ConnectionTerminatorTests
{
	[Fact]
	public async Task TerminateAsync_PublishesTerminateRequest_ToDispatchTerminateSubject()
	{
		var sender = Substitute.For<IMessageSender>();
		var terminator = new ConnectionTerminator(sender);

		var connectionIds = new[] { "conn-a", "conn-b" };
		await terminator.TerminateAsync(connectionIds);

		await sender.Received(1).PublishAsync(
			"dispatch.terminate",
			Arg.Is<byte[]>(bytes => TerminateRequest.Parser.ParseFrom(bytes).ConnectionIds.SequenceEqual(connectionIds)),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task TerminateAsync_SplitsIntoMultiplePublishes_WhenConnectionIdsExceedSizeBudget()
	{
		var sender = Substitute.For<IMessageSender>();
		var terminator = new ConnectionTerminator(sender);

		// 終止請求沒有 payload，所以要靠 connectionId 的數量本身撐破單則訊息的大小上限
		var connectionIds = Enumerable.Range(0, 25_000).Select(i => $"conn-{i}").ToArray();

		var publishedBatches = new List<string[]>();
		sender
			.PublishAsync(
				Arg.Any<string>(),
				Arg.Do<byte[]>(bytes => publishedBatches.Add(TerminateRequest.Parser.ParseFrom(bytes).ConnectionIds.ToArray())),
				Arg.Any<IEnumerable<MessageHeaderValue>>(),
				Arg.Any<CancellationToken>())
			.Returns(ValueTask.CompletedTask);

		await terminator.TerminateAsync(connectionIds);

		Assert.True(publishedBatches.Count > 1, "Expected the large connection id batch to be split across multiple publishes.");
		Assert.Equal(connectionIds, publishedBatches.SelectMany(b => b));
	}

	[Fact]
	public async Task TerminateAsync_PublishesNothing_WhenNoConnectionIdsGiven()
	{
		var sender = Substitute.For<IMessageSender>();
		var terminator = new ConnectionTerminator(sender);

		await terminator.TerminateAsync([]);

		await sender.DidNotReceive().PublishAsync(
			Arg.Any<string>(),
			Arg.Any<byte[]>(),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}
}
