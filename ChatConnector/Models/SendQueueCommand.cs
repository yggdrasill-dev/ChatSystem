using Google.Protobuf;

namespace ChatConnector.Models;

public struct SendQueueCommand
{
	public string Subject { get; set; }

	public string SessionId { get; set; }

	public ByteString Payload { get; set; }
}
