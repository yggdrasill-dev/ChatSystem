using Google.Protobuf;

namespace Common.Protocol;

// 一個訊息型別的註冊資訊，由 AddPacketHandler / AddOutboundPacket 產生，
// PacketRegistry 在啟動時彙總成 subject ↔ 型別的雙向對應表。
public sealed record PacketRegistration(
	string Subject,
	Type MessageType,
	// 只用於下行的訊息型別沒有 handler，這裡是 null。
	Func<IServiceProvider, CommandContext, ByteString, CancellationToken, ValueTask>? Dispatch);
