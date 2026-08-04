using Google.Protobuf;

namespace Common.Protocol;

// subject ↔ 訊息型別的唯一對應表，入站解析與出站序列化共用同一份。
// 協定層本身不認識任何具體命令型別——內容全部來自各層自己的註冊（見 protocol-layer.md ADR-4）。
public sealed class PacketRegistry
{
	private readonly Dictionary<string, PacketRegistration> m_BySubject;
	private readonly Dictionary<Type, string> m_SubjectByType;

	public PacketRegistry(IEnumerable<PacketRegistration> registrations)
	{
		m_BySubject = [];
		m_SubjectByType = [];

		foreach (var registration in registrations)
		{
			// 重複註冊直接 fail fast。ADR-4 用 registry 換掉 oneof 大信封的代價就是失去編譯期
			// exhaustiveness，啟動時的這兩個檢查是僅有的補償，不能省。
			if (!m_BySubject.TryAdd(registration.Subject, registration))
				throw new InvalidOperationException(
					$"Subject '{registration.Subject}' is registered more than once (by {m_BySubject[registration.Subject].MessageType.Name} and {registration.MessageType.Name}).");

			if (!m_SubjectByType.TryAdd(registration.MessageType, registration.Subject))
				throw new InvalidOperationException(
					$"Message type '{registration.MessageType.Name}' is registered under more than one subject ('{m_SubjectByType[registration.MessageType]}' and '{registration.Subject}').");
		}
	}

	public IReadOnlyCollection<string> Subjects => m_BySubject.Keys;

	// 「有註冊、而且有 inbound handler」。只用於下行的 subject 會回 false——client 送那種
	// subject 上來，對協定層而言就跟未知 subject 一樣不該被處理。
	public bool IsInboundSubject(string subject) =>
		m_BySubject.TryGetValue(subject, out var registration) && registration.Dispatch is not null;

	// 出站用：型別 → subject。呼叫端因此不需要自己寫 subject 字串。
	public string ResolveSubject(Type messageType) =>
		m_SubjectByType.TryGetValue(messageType, out var subject)
			? subject
			: throw new InvalidOperationException(
				$"Message type '{messageType.Name}' has no registered subject. Register it with AddPacketHandler or AddOutboundPacket.");

	// 入站用：解析 payload 後從 scope 取出 handler 呼叫。
	// 解析失敗會丟 InvalidProtocolBufferException，由呼叫端決定政策（見 ADR-8）。
	public ValueTask DispatchAsync(
		IServiceProvider services,
		string subject,
		CommandContext context,
		ByteString payload,
		CancellationToken cancellationToken = default)
	{
		if (!m_BySubject.TryGetValue(subject, out var registration) || registration.Dispatch is null)
			throw new InvalidOperationException($"Subject '{subject}' has no inbound handler. Check IsInboundSubject first.");

		return registration.Dispatch(services, context, payload, cancellationToken);
	}
}
