using Chat.Protos;
using Common.Connections;
using Common.Delivery;
using Common.Protocol;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CommandRouter.Tests;

// 用既有的 proto 型別（Packet）當測試用的命令型別，避免為測試多開一份 .proto。
public class InboundProcessorTests
{
	private const string TestSubject = "test.command";

	[Fact]
	public async Task HandleAsync_DispatchesToTheHandler_AndAcksOk()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, RecordingHandler>(TestSubject));

		var command = new Packet { Subject = "inner", Payload = ByteString.CopyFromUtf8("hi") };
		var ack = await host.HandleAsync("conn-1", TestSubject, command.ToByteString());

		Assert.Equal(InboundAck.Types.Status.Ok, ack.Status);
		Assert.Equal(("conn-1", "user-1", "inner"), Assert.Single(host.Sink.Calls));
	}

	[Fact]
	public async Task HandleAsync_PassesThePrincipalFromThePacket_ToTheHandler()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, RecordingHandler>(TestSubject));

		await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString(), principal: "user-42");

		// 「這則命令是誰送的」隨封包一起到，CommandRouter 不查任何對照表（ADR-9）
		Assert.Equal("user-42", Assert.Single(host.Sink.Calls).Principal);
	}

	[Fact]
	public async Task HandleAsync_AcksUnknownSubject()
	{
		using var host = CreateHost();

		var ack = await host.HandleAsync("conn-1", "nobody.registered.this", ByteString.Empty);

		// rolling deploy 期間「新 client + 舊 CommandRouter」是常態，不能因此斷線（ADR-8）
		Assert.Equal(InboundAck.Types.Status.UnknownSubject, ack.Status);
	}

	[Fact]
	public async Task HandleAsync_AcksUnknownSubject_ForAnOutboundOnlySubject()
	{
		using var host = CreateHost(services => services.AddOutboundPacket<Packet>("test.reply"));

		var ack = await host.HandleAsync("conn-1", "test.reply", ByteString.Empty);

		Assert.Equal(InboundAck.Types.Status.UnknownSubject, ack.Status);
	}

	[Fact]
	public async Task HandleAsync_AcksMalformedPayload_WithoutDispatching()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, RecordingHandler>(TestSubject));

		// 0x08 是「field 1, varint」的 tag 但後面沒有值，protobuf 解析必定失敗
		var ack = await host.HandleAsync("conn-1", TestSubject, ByteString.CopyFrom(0x08));

		// terminate 會造成「重連→送同一個壞封包→又被踢」的緊迫迴圈，成本比忽略更高（ADR-8）
		Assert.Equal(InboundAck.Types.Status.MalformedPayload, ack.Status);
		Assert.Empty(host.Sink.Calls);
	}

	[Fact]
	public async Task HandleAsync_AcksHandlerFailed_WhenTheHandlerThrows()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, ThrowingHandler>(TestSubject));

		var ack = await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString());

		// handler 的 bug 不該關掉使用者的連線
		Assert.Equal(InboundAck.Types.Status.HandlerFailed, ack.Status);
		Assert.Equal(nameof(InvalidOperationException), ack.Detail);
	}

	[Fact]
	public void InboundProcessor_HasNoWayToTerminateAConnection()
	{
		// ADR-8：協定層對壞輸入一律是 log + 忽略。唯一的終止路徑是 IInboundFilter 的
		// Terminate，而那個機制已隨 ADR-6 的了斷條件移除——上面那些測試因此不必再各自
		// 斷言 DidNotReceive()，這件事變成結構上不可能發生。
		Assert.DoesNotContain(
			typeof(IConnectionTerminator),
			typeof(InboundProcessor).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
	}

	private static ProcessorHost CreateHost(Action<IServiceCollection>? configure = null)
	{
		var services = new ServiceCollection();
		var sink = new CallSink();

		services.AddSingleton(sink);
		services.AddSingleton(Substitute.For<IOutboundGateway>());
		configure?.Invoke(services);
		services.AddPacketRegistry();

		var provider = services.BuildServiceProvider();

		var processor = new InboundProcessor(
			provider.GetRequiredService<IServiceScopeFactory>(),
			provider.GetRequiredService<PacketRegistry>(),
			NullLogger<InboundProcessor>.Instance);

		return new ProcessorHost(provider, processor, sink);
	}

	private sealed class ProcessorHost(
		ServiceProvider provider,
		InboundProcessor processor,
		CallSink sink) : IDisposable
	{
		public CallSink Sink => sink;

		public async Task<InboundAck> HandleAsync(
			string connectionId,
			string subject,
			ByteString payload,
			string principal = "user-1")
		{
			var packet = new InboundPacket
			{
				ConnectionId = connectionId,
				Principal = principal,
				Subject = subject,
				Payload = payload
			};

			var reply = await processor.HandleAsync("command.inbound", packet.ToByteArray(), null);

			return InboundAck.Parser.ParseFrom(reply);
		}

		public void Dispose() => provider.Dispose();
	}

	private sealed class CallSink
	{
		public List<(string ConnectionId, string Principal, string Subject)> Calls { get; } = [];
	}

	private sealed class RecordingHandler(CallSink sink) : IPacketHandler<Packet>
	{
		public ValueTask HandleAsync(CommandContext context, Packet message, CancellationToken cancellationToken = default)
		{
			sink.Calls.Add((context.ConnectionId, context.Principal, message.Subject));

			return ValueTask.CompletedTask;
		}
	}

	private sealed class ThrowingHandler : IPacketHandler<Packet>
	{
		public ValueTask HandleAsync(CommandContext context, Packet message, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("boom");
	}
}
