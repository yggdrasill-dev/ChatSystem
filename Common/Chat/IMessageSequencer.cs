namespace Common.Chat;

// 發號的接縫。今天是程序內的時鐘，明天可能是房間 actor（chat-layer.md ADR-9）——
// 把它獨立成介面，換擁有者時動的是 DI 註冊那一行。
public interface IMessageSequencer
{
	long Next();
}

// 微秒時鐘 + 單調守衛。**保證單調與唯一，不保證連續**（ADR-2）。
internal sealed class MonotonicMicrosecondSequencer(TimeProvider timeProvider) : IMessageSequencer
{
	private long m_Last;

	public long Next()
	{
		var observed = timeProvider.GetUtcNow().ToUnixTimeMilliseconds() * 1000L;

		// 這個 CAS 迴圈是必要的，不是防禦性寫法。一個 CommandRouter process 裡有多個 handler
		// 併發，非原子的 `m_Last = Math.Max(observed, m_Last + 1)` 會讓兩個執行緒讀到同一個
		// prior 而發出同一個號——那會把「主鍵衝突」從幾乎不可能變成經常發生。
		//
		// 非原子的版本在單執行緒測試下永遠會過，所以這裡必須有併發測試盯著
		// （MonotonicMicrosecondSequencerTests）。
		long prior, next;

		do
		{
			prior = Volatile.Read(ref m_Last);
			next = Math.Max(observed, prior + 1);
		}
		while (Interlocked.CompareExchange(ref m_Last, next, prior) != prior);

		return next;
	}
}
