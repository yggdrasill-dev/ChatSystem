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
		await host.Terminator.DidNotReceive().TerminateAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_PassesThePrincipalFromThePacket_ToBothFiltersAndHandlers()
	{
		var seenPrincipals = new List<string>();
		using var host = CreateHost(services =>
		{
			services.AddPacketHandler<Packet, RecordingHandler>(TestSubject);
			services.AddSingleton(new FilterScript
			{
				Order = 0,
				Decision = FilterDecision.Allow,
				SeenPrincipals = seenPrincipals
			});
			services.AddInboundFilter<ScriptedFilter>();
		});

		await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString(), principal: "user-42");

		// 「這則命令是誰送的」隨封包一起到，CommandRouter 不查任何對照表（ADR-9）
		Assert.Equal(["user-42"], seenPrincipals);
		Assert.Equal("user-42", Assert.Single(host.Sink.Calls).Principal);
	}

	[Fact]
	public async Task HandleAsync_AcksUnknownSubject_WithoutTerminating()
	{
		using var host = CreateHost();

		var ack = await host.HandleAsync("conn-1", "nobody.registered.this", ByteString.Empty);

		// rolling deploy 期間「新 client + 舊 CommandRouter」是常態，不能因此斷線（ADR-8）
		Assert.Equal(InboundAck.Types.Status.UnknownSubject, ack.Status);
		await host.Terminator.DidNotReceive().TerminateAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_AcksUnknownSubject_ForAnOutboundOnlySubject()
	{
		using var host = CreateHost(services => services.AddOutboundPacket<Packet>("test.reply"));

		var ack = await host.HandleAsync("conn-1", "test.reply", ByteString.Empty);

		Assert.Equal(InboundAck.Types.Status.UnknownSubject, ack.Status);
	}

	[Fact]
	public async Task HandleAsync_AcksMalformedPayload_WithoutTerminating()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, RecordingHandler>(TestSubject));

		// 0x08 是「field 1, varint」的 tag 但後面沒有值，protobuf 解析必定失敗
		var ack = await host.HandleAsync("conn-1", TestSubject, ByteString.CopyFrom(0x08));

		// terminate 會造成「重連→送同一個壞封包→又被踢」的緊迫迴圈，成本比忽略更高（ADR-8）
		Assert.Equal(InboundAck.Types.Status.MalformedPayload, ack.Status);
		Assert.Empty(host.Sink.Calls);
		await host.Terminator.DidNotReceive().TerminateAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_TerminatesTheConnection_WhenAFilterSaysSo()
	{
		using var host = CreateHost(services =>
		{
			services.AddPacketHandler<Packet, RecordingHandler>(TestSubject);
			services.AddSingleton(new FilterScript { Order = 0, Decision = FilterDecision.Terminate });
			services.AddInboundFilter<ScriptedFilter>();
		});

		var ack = await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString());

		Assert.Equal(InboundAck.Types.Status.RejectedByFilter, ack.Status);
		Assert.Empty(host.Sink.Calls);
		// 處置動作由 processor 統一執行，filter 只負責判斷（ADR-6）
		await host.Terminator.Received(1).TerminateAsync(
			Arg.Is<IReadOnlyCollection<string>>(ids => ids != null && ids.SequenceEqual(new[] { "conn-1" })),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_AcksRejected_WithoutTerminating_WhenAFilterDrops()
	{
		using var host = CreateHost(services =>
		{
			services.AddPacketHandler<Packet, RecordingHandler>(TestSubject);
			services.AddSingleton(new FilterScript { Order = 0, Decision = FilterDecision.Drop });
			services.AddInboundFilter<ScriptedFilter>();
		});

		var ack = await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString());

		Assert.Equal(InboundAck.Types.Status.RejectedByFilter, ack.Status);
		Assert.Empty(host.Sink.Calls);
		await host.Terminator.DidNotReceive().TerminateAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RunsFiltersInOrder_AndStopsAtTheFirstRejection()
	{
		var evaluated = new List<int>();
		using var host = CreateHost(services =>
		{
			services.AddPacketHandler<Packet, RecordingHandler>(TestSubject);
			services.AddSingleton(new FilterScript { Order = 0, Decision = FilterDecision.Drop, Evaluated = evaluated });
			services.AddSingleton(new FilterScript { Order = -1, Decision = FilterDecision.Allow, Evaluated = evaluated });
			services.AddScoped<IInboundFilter, ScriptedFilter>();
			services.AddScoped<IInboundFilter, SecondScriptedFilter>();
		});

		await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString());

		// Order -1 先跑（Allow），Order 0 接著跑並 Drop，之後不再有 filter 被評估
		Assert.Equal([-1, 0], evaluated);
	}

	[Fact]
	public async Task HandleAsync_AcksHandlerFailed_WhenTheHandlerThrows()
	{
		using var host = CreateHost(services => services.AddPacketHandler<Packet, ThrowingHandler>(TestSubject));

		var ack = await host.HandleAsync("conn-1", TestSubject, new Packet().ToByteString());

		// handler 的 bug 不該關掉使用者的連線
		Assert.Equal(InboundAck.Types.Status.HandlerFailed, ack.Status);
		Assert.Equal(nameof(InvalidOperationException), ack.Detail);
		await host.Terminator.DidNotReceive().TerminateAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
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
		var terminator = Substitute.For<IConnectionTerminator>();

		var processor = new InboundProcessor(
			provider.GetRequiredService<IServiceScopeFactory>(),
			provider.GetRequiredService<PacketRegistry>(),
			terminator,
			NullLogger<InboundProcessor>.Instance);

		return new ProcessorHost(provider, processor, sink, terminator);
	}

	private sealed class ProcessorHost(
		ServiceProvider provider,
		InboundProcessor processor,
		CallSink sink,
		IConnectionTerminator terminator) : IDisposable
	{
		public CallSink Sink => sink;

		public IConnectionTerminator Terminator => terminator;

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

	private sealed class FilterScript
	{
		public int Order { get; init; }

		public FilterDecision Decision { get; init; }

		public List<int>? Evaluated { get; init; }

		public List<string>? SeenPrincipals { get; init; }
	}

	// 兩個 filter 型別是為了讓 DI 能同時註冊兩個 IInboundFilter 實作；
	// 各自從註冊的 FilterScript 清單裡按 Order 取自己那一份。
	private class ScriptedFilter(IEnumerable<FilterScript> scripts) : IInboundFilter
	{
		protected virtual int ScriptIndex => 0;

		private FilterScript Script => scripts.OrderBy(script => script.Order).ElementAt(ScriptIndex);

		public int Order => Script.Order;

		public ValueTask<FilterDecision> EvaluateAsync(
			CommandContext context,
			string subject,
			CancellationToken cancellationToken = default)
		{
			Script.Evaluated?.Add(Script.Order);
			Script.SeenPrincipals?.Add(context.Principal);

			return ValueTask.FromResult(Script.Decision);
		}
	}

	private sealed class SecondScriptedFilter(IEnumerable<FilterScript> scripts) : ScriptedFilter(scripts)
	{
		protected override int ScriptIndex => 1;
	}
}
