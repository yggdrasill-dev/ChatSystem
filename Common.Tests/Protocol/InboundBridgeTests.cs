using Adaptare;
using Chat.Protos;
using Common.Protocol;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Common.Tests.Protocol;

public class InboundBridgeTests
{
	[Fact]
	public async Task HandleAsync_RequestsAnInboundPacket_OnTheCommandInboundSubject()
	{
		var sender = Substitute.For<IMessageSender>();
		StubReply(sender, new InboundAck { Status = InboundAck.Types.Status.Ok });

		var bridge = new InboundBridge(sender, NullLogger<InboundBridge>.Instance);

		await bridge.HandleAsync("conn-1", "identity.bind", ByteString.CopyFromUtf8("token"));

		await sender.Received(1).RequestAsync<byte[], byte[]>(
			"command.inbound",
			Arg.Is<byte[]>(bytes => Matches(bytes, "conn-1", "identity.bind", "token")),
			Arg.Any<IEnumerable<MessageHeaderValue>>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_DoesNotThrow_WhenTheAckReportsThatNothingWasProcessed()
	{
		var sender = Substitute.For<IMessageSender>();
		StubReply(sender, new InboundAck { Status = InboundAck.Types.Status.UnknownSubject, Detail = "nope" });

		var bridge = new InboundBridge(sender, NullLogger<InboundBridge>.Instance);

		// 未知 subject / payload 畸形都不該讓連線斷掉（ADR-8），bridge 只記 log
		await bridge.HandleAsync("conn-1", "nope", ByteString.Empty);
	}

	[Fact]
	public async Task HandleAsync_Rethrows_WhenTheCommandRouterNeverReplies()
	{
		var sender = Substitute.For<IMessageSender>();
		sender
			.RequestAsync<byte[], byte[]>(
				Arg.Any<string>(),
				Arg.Any<byte[]>(),
				Arg.Any<IEnumerable<MessageHeaderValue>>(),
				Arg.Any<CancellationToken>())
			.Throws(new OperationCanceledException());

		var bridge = new InboundBridge(sender, NullLogger<InboundBridge>.Instance);

		// 逾時往上丟會讓連線關閉（client 重連重送）。刻意不吞掉：只記 log 然後繼續讀下一個
		// frame 的話，這則訊息可能稍後才被處理，單連線順序保證就破了。
		await Assert.ThrowsAsync<OperationCanceledException>(
			async () => await bridge.HandleAsync("conn-1", "identity.bind", ByteString.Empty));
	}

	private static void StubReply(IMessageSender sender, InboundAck ack) =>
		sender
			.RequestAsync<byte[], byte[]>(
				Arg.Any<string>(),
				Arg.Any<byte[]>(),
				Arg.Any<IEnumerable<MessageHeaderValue>>(),
				Arg.Any<CancellationToken>())
			.Returns(ack.ToByteArray());

	private static bool Matches(byte[]? bytes, string connectionId, string subject, string payloadText)
	{
		var packet = InboundPacket.Parser.ParseFrom(bytes);

		return packet.ConnectionId == connectionId
			&& packet.Subject == subject
			&& packet.Payload.ToStringUtf8() == payloadText;
	}
}
