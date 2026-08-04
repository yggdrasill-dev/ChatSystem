using Chat.Protos;
using Common.Delivery;
using Common.Protocol;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Common.Tests.Protocol;

public class ProtocolLayerRegistrationTests
{
	[Fact]
	public async Task AddPacketHandler_WiresTheParser_AndResolvesTheHandlerFromTheScope()
	{
		var sink = new CallSink();
		using var provider = BuildProvider(services =>
		{
			services.AddSingleton(sink);
			services.AddPacketHandler<Packet, RecordingHandler>("test.command");
		});

		var registry = provider.GetRequiredService<PacketRegistry>();

		Assert.True(registry.IsInboundSubject("test.command"));
		Assert.Equal("test.command", registry.ResolveSubject(typeof(Packet)));

		var message = new Packet { Subject = "inner", Payload = ByteString.CopyFromUtf8("hi") };

		using var scope = provider.CreateScope();
		await registry.DispatchAsync(
			scope.ServiceProvider,
			"test.command",
			new CommandContext("conn-1", "user-1"),
			message.ToByteString());

		// handler 拿到的是已經解析好的訊息，不是 bytes；principal 隨 context 一起到
		Assert.Equal(("conn-1", "user-1", "inner", "hi"), Assert.Single(sink.Calls));
	}

	[Fact]
	public void AddPacketHandler_RegistersTheHandlerAsScoped()
	{
		using var provider = BuildProvider(services =>
		{
			services.AddSingleton(new CallSink());
			services.AddPacketHandler<Packet, RecordingHandler>("test.command");
		});

		using var first = provider.CreateScope();
		using var second = provider.CreateScope();

		Assert.NotSame(
			first.ServiceProvider.GetRequiredService<RecordingHandler>(),
			second.ServiceProvider.GetRequiredService<RecordingHandler>());
		Assert.Same(
			first.ServiceProvider.GetRequiredService<RecordingHandler>(),
			first.ServiceProvider.GetRequiredService<RecordingHandler>());
	}

	[Fact]
	public void AddOutboundPacket_RegistersSubjectWithoutAHandler()
	{
		using var provider = BuildProvider(services => services.AddOutboundPacket<Packet>("test.reply"));

		var registry = provider.GetRequiredService<PacketRegistry>();

		Assert.Equal("test.reply", registry.ResolveSubject(typeof(Packet)));
		Assert.False(registry.IsInboundSubject("test.reply"));
	}

	[Fact]
	public void AddPacketRegistry_FailsFast_WhenTwoLayersClaimTheSameSubject()
	{
		var provider = BuildProvider(services =>
		{
			services.AddSingleton(new CallSink());
			services.AddPacketHandler<Packet, RecordingHandler>("test.command");
			services.AddOutboundPacket<TerminateRequest>("test.command");
		});

		// ADR-4 用 registry 換掉 oneof 大信封的代價是失去編譯期 exhaustiveness，
		// 啟動時 fail fast 是僅有的補償
		using (provider)
			Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<PacketRegistry>());
	}

	private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
	{
		var services = new ServiceCollection();

		services.AddSingleton(Substitute.For<IOutboundGateway>());
		configure(services);
		services.AddPacketRegistry();

		return services.BuildServiceProvider();
	}

	private sealed class CallSink
	{
		public List<(string ConnectionId, string Principal, string Subject, string Payload)> Calls { get; } = [];
	}

	private sealed class RecordingHandler(CallSink sink) : IPacketHandler<Packet>
	{
		public ValueTask HandleAsync(CommandContext context, Packet message, CancellationToken cancellationToken = default)
		{
			sink.Calls.Add((context.ConnectionId, context.Principal, message.Subject, message.Payload.ToStringUtf8()));

			return ValueTask.CompletedTask;
		}
	}
}
