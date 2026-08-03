using Google.Protobuf;

namespace Common.Protocol;

// 各層/各功能自己實作的命令 handler，向 CommandRouter 註冊。
// subject 比對與 payload 解析都由協定層做完，handler 只會拿到已經解析好的訊息。
//
// TMessage 的 new() 約束是為了讓協定層自己建 MessageParser<TMessage>，
// handler 因此不用宣告 parser（protobuf 產生的類別有 static Parser，但泛型約束拿不到它）。
public interface IPacketHandler<TMessage> where TMessage : IMessage<TMessage>, new()
{
	ValueTask HandleAsync(string connectionId, TMessage message, CancellationToken cancellationToken = default);
}
