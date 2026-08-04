using Adaptare;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Integration.Tests;

// 先確認 Adaptare.Direct 的註冊形狀與 exchange 語意，再拿它去跑房間層的組合測試。
//
// 這裡刻意也把「重複註冊」與「沒有 exchange」兩種情況寫成測試——那正是真 NATS 上花掉最多
// 時間的兩個 bug，而它們在 Direct 上應該用毫秒就能重現。
public class DirectMessagingProbeTests
{
	private const string Subject = "probe.command";

	[Fact]
	public async Task Publish_ReachesTheHandler_WhenTheExchangeIsRegisteredAfterTheQueue()
	{
		var sink = new Sink();
		using var host = Build(sink, ExchangePosition.AfterQueue);

		await host.Services.GetRequiredService<IMessageSender>().PublishAsync(Subject, "hello"u8.ToArray());

		Assert.Equal(1, await sink.WaitForAsync(1));
	}

	[Fact]
	public async Task Publish_ThrowsFastWhenNoExchangeMatchesTheSubject()
	{
		var sink = new Sink();
		using var host = Build(sink, ExchangePosition.None);

		// Adaptare 本來就有這個護欄——「找不到 sender」是明確的例外，不是靜默丟掉。
		// 這也推翻了「斷線事件是因為沒有 exchange 而消失」的假設：那會 throw，而當時沒有例外。
		var ex = await Assert.ThrowsAsync<MessageSenderNotFoundException>(
			async () => await host.Services.GetRequiredService<IMessageSender>()
				.PublishAsync(Subject, "hello"u8.ToArray()));

		Assert.Contains(Subject, ex.Message);
		Assert.Equal(0, sink.Count);
	}

	[Fact]
	public async Task Publish_AlsoReachesTheHandler_WhenTheExchangeIsRegisteredBeforeTheQueue()
	{
		var sink = new Sink();
		using var host = Build(sink, ExchangePosition.BeforeQueue);

		await host.Services.GetRequiredService<IMessageSender>().PublishAsync(Subject, "hello"u8.ToArray());

		// 註冊順序不影響。這條推翻了「斷線事件消失是因為我把 exchange 從 queue 之後搬到之前」
		// 這個假設——至少在 Direct 上，兩種順序都送得到。
		Assert.Equal(1, await sink.WaitForAsync(1));
	}

	[Fact]
	public async Task Publish_ReachesTheHandlerOnce_EvenWhenTheQueueIsRegisteredTwice()
	{
		var sink = new Sink();
		using var host = Build(sink, ExchangePosition.AfterQueue, duplicateQueue: true);

		await host.Services.GetRequiredService<IMessageSender>().PublishAsync(Subject, "hello"u8.ToArray());

		// Direct 對重複註冊是 idempotent 的——所以它**重現不了** NATS 那個「每則下行訊息被
		// 投遞兩次」的 bug。這條記錄下來，免得有人以為 Direct 的測試能守住那個回歸。
		Assert.Equal(1, await sink.WaitForAsync(2, TimeSpan.FromMilliseconds(500)));
	}

	private enum ExchangePosition
	{
		None,
		BeforeQueue,
		AfterQueue,
	}

	private static IHost Build(Sink sink, ExchangePosition exchange, bool duplicateQueue = false)
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Logging.ClearProviders();

		builder.Services.AddSingleton(sink);

		var services = builder.Services.AddMessageQueue();

		if (exchange == ExchangePosition.BeforeQueue)
			services = services.AddDirectGlobPatternExchange("*");

		services = services.AddDirectMessageQueue(config => config.AddHandler<RecordingHandler>(Subject));

		if (duplicateQueue)
			services = services.AddDirectMessageQueue(config => config.AddHandler<RecordingHandler>(Subject));

		if (exchange == ExchangePosition.AfterQueue)
			services.AddDirectGlobPatternExchange("*");

		var host = builder.Build();
		host.Start();

		return host;
	}

	private sealed class Sink
	{
		private int m_Count;

		public int Count => Volatile.Read(ref m_Count);

		public void Record() => Interlocked.Increment(ref m_Count);

		public async Task<int> WaitForAsync(int expected, TimeSpan? timeout = null)
		{
			var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));

			while (DateTime.UtcNow < deadline && Count < expected)
				await Task.Delay(20);

			return Count;
		}
	}

	private sealed class RecordingHandler(Sink sink) : IMessageHandler<byte[]>
	{
		public ValueTask HandleAsync(
			string subject,
			byte[] data,
			IEnumerable<MessageHeaderValue>? headerValues,
			CancellationToken cancellationToken = default)
		{
			sink.Record();

			return ValueTask.CompletedTask;
		}
	}
}
