using System.Net.WebSockets;

namespace Gateway.Tests.TestSupport;

// 最小的 WebSocket test double：只記錄送出的 frame，不真的連網路。
internal sealed class RecordingFakeWebSocket : WebSocket
{
	public List<byte[]> SentFrames { get; } = [];

	public override WebSocketCloseStatus? CloseStatus => null;

	public override string? CloseStatusDescription => null;

	private WebSocketState m_State = WebSocketState.Open;

	public override WebSocketState State => m_State;

	public override string? SubProtocol => null;

	public override void Abort() => m_State = WebSocketState.Aborted;

	public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
	{
		m_State = WebSocketState.Closed;
		return Task.CompletedTask;
	}

	public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
	{
		m_State = WebSocketState.CloseSent;
		return Task.CompletedTask;
	}

	public override void Dispose()
	{
	}

	public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
		throw new NotSupportedException("這個 test double 目前不需要模擬 receive。");

	public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
	{
		SentFrames.Add(buffer.ToArray());
		return Task.CompletedTask;
	}
}
