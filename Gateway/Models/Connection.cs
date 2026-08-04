using System.Net.WebSockets;
using Chat.Protos;
using Google.Protobuf;

namespace Gateway.Models;

// 封裝單一 socket 的生命週期與送封包行為，框裝邏輯只在這裡寫一次。
public sealed class Connection(string connectionId, string principal, WebSocket socket)
{
	// WebSocket 同一時間只能有一個 outstanding 的 send 呼叫，SendAsync/CloseAsync
	// 都會送出 frame，兩者也要互相排隊，否則並行呼叫會噴 InvalidOperationException
	// 或把 frame 搞壞。
	private readonly SemaphoreSlim m_SendLock = new(1, 1);

	public string ConnectionId { get; } = connectionId;

	// handshake 驗證通過的不透明字串。連線層不解讀它，只在每則 inbound 訊息與斷線事件上
	// 轉交出去——存在這裡的意義就是「不用為每則命令查一次 Redis」。
	public string Principal { get; } = principal;

	public WebSocketState State => socket.State;

	public async ValueTask SendAsync(string subject, ByteString payload, CancellationToken cancellationToken = default)
	{
		var packet = new Packet { Subject = subject, Payload = payload };
		var data = packet.ToByteArray();

		await m_SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await socket.SendAsync(data, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			m_SendLock.Release();
		}
	}

	public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default) =>
		socket.ReceiveAsync(buffer, cancellationToken);

	public async Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken = default)
	{
		await m_SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await socket.CloseAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			m_SendLock.Release();
		}
	}

	// 由伺服器主動發起關閉時要用這個，不能用 CloseAsync：CloseAsync 會在送出 close frame 後
	// 等待對方回應的 close frame，而 receive loop 同時也在 ReceiveAsync，兩邊會搶同一個 receive。
	// CloseOutputAsync 只送出自己的 close frame，剩下的交給 receive loop 照既有流程收尾。
	public async Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken = default)
	{
		await m_SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			await socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			m_SendLock.Release();
		}
	}
}
