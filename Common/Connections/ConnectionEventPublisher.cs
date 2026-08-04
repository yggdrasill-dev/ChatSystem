using Adaptare;
using Chat.Protos;
using Google.Protobuf;

namespace Common.Connections;

internal sealed class ConnectionEventPublisher(IMessageSender messageSender) : IConnectionEventPublisher
{
	// 刻意不放在 connect.* 家族裡：那個前綴目前的意思是「投遞給某個 Gateway 節點」
	// （connect.deliver.{nodeId}、connect.terminate.{nodeId}），而這是反方向的事件廣播，
	// 沒有特定目標角色。用 events.* 開一個明確的事件命名空間，也避免 connect./connection.
	// 只差三個字母的辨識風險。
	private const string DisconnectedSubject = "events.connection.disconnected";

	public ValueTask PublishDisconnectedAsync(
		string connectionId,
		string nodeId,
		string principal,
		CancellationToken cancellationToken = default)
	{
		var message = new ConnectionDisconnected
		{
			ConnectionId = connectionId,
			NodeId = nodeId,
			Principal = principal
		};

		return messageSender.PublishAsync(DisconnectedSubject, message.ToByteArray(), cancellationToken);
	}
}
