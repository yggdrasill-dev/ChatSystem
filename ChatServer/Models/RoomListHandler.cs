using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using NATS.Client;

namespace ChatServer.Models
{
	public class RoomListHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly IQueryService<ListRoomQuery, string> m_ListRoomService;

		public RoomListHandler(
			IMessageQueueService messageQueueService,
			IQueryService<ListRoomQuery, string> listRoomService)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_ListRoomService = listRoomService ?? throw new ArgumentNullException(nameof(listRoomService));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var packet = QueuePacket.Parser.ParseFrom(msg.Data);

			var rooms = m_ListRoomService.QueryAsync(new ListRoomQuery());

			var response = new RoomList();

			response.Rooms.AddRange(rooms.ToEnumerable());

			var sendMsg = new SendPacket
			{
				Subject = "chat.room.list"
			};

			sendMsg.SessionIds.Add(packet.SessionId);
			sendMsg.Payload = response.ToByteString();

			await m_MessageQueueService
				.PublishAsync("connect.send", sendMsg.ToByteArray())
				.ConfigureAwait(false);
		}
	}
}
