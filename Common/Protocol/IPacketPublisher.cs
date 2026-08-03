using Google.Protobuf;

namespace Common.Protocol;

// 出口：上層只給「要送給誰」跟「送什麼訊息」，subject 由 registry 反查。
// 呼叫端因此不需要自己寫 subject 字串、也不需要自己序列化。
// 下行一律經 IOutboundGateway → Dispatcher，協定層不開第二條通道（見 protocol-layer.md ADR-5）。
public interface IPacketPublisher
{
	ValueTask PublishAsync<TMessage>(
		IReadOnlyCollection<string> connectionIds,
		TMessage message,
		CancellationToken cancellationToken = default)
		where TMessage : IMessage<TMessage>;
}
