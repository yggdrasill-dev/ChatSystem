using Chat.Protos;
using Common.Protocol;

namespace Common.Tests.Protocol;

// 用既有的 proto 型別（Packet / TerminateRequest）當測試用的命令型別，
// 避免為了測試多開一份 .proto 與 build 設定。
public class PacketRegistryTests
{
	[Fact]
	public void Constructor_Throws_WhenTheSameSubjectIsRegisteredTwice()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => new PacketRegistry([
			Inbound("test.command", typeof(Packet)),
			Inbound("test.command", typeof(TerminateRequest)),
		]));

		Assert.Contains("test.command", ex.Message);
	}

	[Fact]
	public void Constructor_Throws_WhenTheSameMessageTypeIsRegisteredUnderTwoSubjects()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => new PacketRegistry([
			Inbound("test.one", typeof(Packet)),
			Inbound("test.two", typeof(Packet)),
		]));

		Assert.Contains(nameof(Packet), ex.Message);
	}

	[Fact]
	public void ResolveSubject_ReturnsTheRegisteredSubject()
	{
		var registry = new PacketRegistry([Outbound("chat.receive", typeof(Packet))]);

		Assert.Equal("chat.receive", registry.ResolveSubject(typeof(Packet)));
	}

	[Fact]
	public void ResolveSubject_Throws_ForAnUnregisteredType()
	{
		var registry = new PacketRegistry([]);

		Assert.Throws<InvalidOperationException>(() => registry.ResolveSubject(typeof(Packet)));
	}

	[Fact]
	public void IsInboundSubject_IsFalse_ForAnOutboundOnlySubject()
	{
		var registry = new PacketRegistry([
			Inbound("test.command", typeof(Packet)),
			Outbound("test.reply", typeof(TerminateRequest)),
		]);

		Assert.True(registry.IsInboundSubject("test.command"));
		// client 送只用於下行的 subject 上來，對協定層而言等同未知 subject
		Assert.False(registry.IsInboundSubject("test.reply"));
		Assert.False(registry.IsInboundSubject("test.nope"));
	}

	[Fact]
	public async Task DispatchAsync_InvokesTheRegisteredDispatch()
	{
		var calls = new List<(string ConnectionId, string Payload)>();
		var registry = new PacketRegistry([
			new PacketRegistration("test.command", typeof(Packet), (_, connectionId, payload, _) =>
			{
				calls.Add((connectionId, payload.ToStringUtf8()));
				return ValueTask.CompletedTask;
			}),
		]);

		await registry.DispatchAsync(
			EmptyServiceProvider.Instance,
			"test.command",
			"conn-1",
			Google.Protobuf.ByteString.CopyFromUtf8("hi"));

		Assert.Equal(("conn-1", "hi"), Assert.Single(calls));
	}

	[Fact]
	public async Task DispatchAsync_Throws_ForAnOutboundOnlySubject()
	{
		var registry = new PacketRegistry([Outbound("test.reply", typeof(Packet))]);

		await Assert.ThrowsAsync<InvalidOperationException>(async () => await registry.DispatchAsync(
			EmptyServiceProvider.Instance,
			"test.reply",
			"conn-1",
			Google.Protobuf.ByteString.Empty));
	}

	[Fact]
	public void Subjects_ListsEverythingRegistered()
	{
		var registry = new PacketRegistry([
			Inbound("test.command", typeof(Packet)),
			Outbound("test.reply", typeof(TerminateRequest)),
		]);

		Assert.Equal(["test.command", "test.reply"], registry.Subjects.OrderBy(subject => subject));
	}

	private static PacketRegistration Inbound(string subject, Type messageType) =>
		new(subject, messageType, (_, _, _, _) => ValueTask.CompletedTask);

	private static PacketRegistration Outbound(string subject, Type messageType) =>
		new(subject, messageType, null);

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public static readonly EmptyServiceProvider Instance = new();

		public object? GetService(Type serviceType) => null;
	}
}
