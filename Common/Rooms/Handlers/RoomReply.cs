using Chat.Protos;

namespace Common.Rooms.Handlers;

internal static class RoomReply
{
	public static RoomOperationReply Of(RoomOperationReply.Types.Status status, string roomId) =>
		new() { Status = status, RoomId = roomId };
}
