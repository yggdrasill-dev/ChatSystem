using System.Net.WebSockets;
using Chat.Protos;
using Google.Protobuf;

namespace Gateway.Models;

// 封裝單一 socket 的生命週期與送封包行為，框裝邏輯只在這裡寫一次。
public sealed class Connection(string connectionId, WebSocket socket)
{
	// WebSocket 同一時間只能有一個 outstanding 的 send 呼叫，SendAsync/CloseAsync
	// 都會送出 frame，兩者也要互相排隊，否則並行呼叫會噴 InvalidOperationException
	// 或把 frame 搞壞。
	private readonly SemaphoreSlim m_SendLock = new(1, 1);

	public string ConnectionId { get; } = connectionId;

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
}
