using System.Net.WebSockets;
using Chat.Protos;
using Google.Protobuf;

namespace Gateway.Models;

// 封裝單一 socket 的生命週期與送封包行為，框裝邏輯只在這裡寫一次。
public sealed class Connection(string connectionId, WebSocket socket)
{
	public string ConnectionId { get; } = connectionId;

	public WebSocketState State => socket.State;

	public async ValueTask SendAsync(string subject, ByteString payload, CancellationToken cancellationToken = default)
	{
		var packet = new Packet { Subject = subject, Payload = payload };

		await socket.SendAsync(
			packet.ToByteArray(),
			WebSocketMessageType.Binary,
			true,
			cancellationToken).ConfigureAwait(false);
	}

	public Task<WebSocketReceiveResult> ReceiveAsync(byte[] buffer, CancellationToken cancellationToken = default) =>
		socket.ReceiveAsync(buffer, cancellationToken);

	public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken = default) =>
		socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
}
