using Common.Chat;

namespace Common.Tests.Chat;

// chat-layer.md §9 明確要求這一組：CAS 迴圈的必要性**在單執行緒測試下永遠看不出來**，
// 非原子的 `m_Last = Math.Max(observed, m_Last + 1)` 也會通過下面每一條循序測試。
public class MonotonicMicrosecondSequencerTests
{
	private static readonly DateTimeOffset _Start = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

	[Fact]
	public void Next_IsStrictlyIncreasing_WithinTheSameClockTick()
	{
		var sequencer = NewSequencer(out _);

		// 時鐘完全不動：所有的號都來自單調守衛，而不是時鐘。
		var keys = Enumerable.Range(0, 1000).Select(_ => sequencer.Next()).ToList();

		Assert.Equal(keys.OrderBy(key => key), keys);
		Assert.Equal(keys.Count, keys.Distinct().Count());
	}

	[Fact]
	public void Next_BorrowsFutureMicroseconds_WhenMessagesArriveInTheSameMillisecond()
	{
		var sequencer = NewSequencer(out _);

		var first = sequencer.Next();
		var second = sequencer.Next();

		// 同一毫秒內到達就一路往後排到未來的微秒上（ADR-2）。偏移只在單一 process 的到達率
		// 超過每微秒一則（= 100 萬則/秒）時才會累積，而每則訊息要走七次網路往返。
		Assert.Equal(_Start.ToUnixTimeMilliseconds() * 1000L, first);
		Assert.Equal(first + 1, second);
	}

	[Fact]
	public void Next_ReadoptsTheClock_OnceRealTimeCatchesUp()
	{
		var sequencer = NewSequencer(out var clock);

		sequencer.Next();
		sequencer.Next();

		clock.Set(_Start.AddSeconds(1));

		// **偏移不跨突發累積**：真實時鐘一追上就重新採用時鐘值，而不是繼續 +1。
		Assert.Equal(_Start.AddSeconds(1).ToUnixTimeMilliseconds() * 1000L, sequencer.Next());
	}

	[Fact]
	public void Next_StaysMonotonic_WhenTheClockJumpsBackwards()
	{
		var sequencer = NewSequencer(out var clock);

		var before = sequencer.Next();

		// NTP 往回修正 5 秒。排序鍵仍然單調（純靠遞增撐過那 5 秒），但同一批訊息的 SentAt
		// 會往回跳——「列表順序」與「顯示時間」在那個窗口內對不上。罕見、會自癒、只是觀感
		// 問題，記在文件裡而不是假裝沒有（ADR-2）。
		clock.Set(_Start.AddSeconds(-5));

		Assert.Equal(before + 1, sequencer.Next());
	}

	[Fact]
	public async Task Next_NeverIssuesTheSameKeyTwice_UnderConcurrency()
	{
		// **這是這個檔案存在的理由。** 一個 CommandRouter process 裡有多個 handler 併發；
		// 非原子的守衛會讓兩個執行緒讀到同一個 prior 而發出同一個號，把「主鍵衝突」從幾乎
		// 不可能變成經常發生。還原 CAS 迴圈的話這條會紅，上面四條不會。
		var sequencer = NewSequencer(out _);

		const int Threads = 16;
		const int PerThread = 2000;

		var issued = await Task.WhenAll(Enumerable
			.Range(0, Threads)
			.Select(_ => Task.Run(() => Enumerable.Range(0, PerThread).Select(_ => sequencer.Next()).ToArray())));

		var all = issued.SelectMany(keys => keys).ToList();

		Assert.Equal(Threads * PerThread, all.Count);
		Assert.Equal(all.Count, all.Distinct().Count());
	}

	private static MonotonicMicrosecondSequencer NewSequencer(out MutableTimeProvider clock)
	{
		clock = new MutableTimeProvider(_Start);

		return new MonotonicMicrosecondSequencer(clock);
	}

	// 時鐘要能往前也能**往回**推（NTP 那一條），所以不是 Advance 而是 Set。
	private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
	{
		private DateTimeOffset m_Now = now;

		public override DateTimeOffset GetUtcNow() => m_Now;

		public void Set(DateTimeOffset value) => m_Now = value;
	}
}
