using Common;
using Google.Protobuf;

namespace Gateway.Models;

// 預設實作：使用者管理層/業務層還沒設計，先只記 log，不做任何事。
// 之後有業務層時，換掉這個 DI 註冊即可，不需要改連線層任何程式碼。
internal sealed class NoOpInboundMessageHandler(ILogger<NoOpInboundMessageHandler> logger) : IInboundMessageHandler
{
	public ValueTask HandleAsync(string connectionId, string subject, ByteString payload, CancellationToken cancellationToken = default)
	{
		logger.LogInformation(
			"Received {Subject} ({PayloadSize} bytes) from {ConnectionId}, no upper-layer handler wired up yet.",
			subject,
			payload.Length,
			connectionId);

		return ValueTask.CompletedTask;
	}
}
