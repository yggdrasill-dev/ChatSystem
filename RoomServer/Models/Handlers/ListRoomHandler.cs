using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using NATS.Client;

namespace RoomServer.Models.Handlers
{
	public class ListRoomHandler : IMessageHandler
	{
		private readonly IQueryService<RoomListQuery, string> m_RoomListService;

		public ListRoomHandler(IQueryService<RoomListQuery, string> roomListService)
		{
			m_RoomListService = roomListService ?? throw new ArgumentNullException(nameof(roomListService));
		}

		public ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var rooms = m_RoomListService.QueryAsync(new RoomListQuery());

			var response = new RoomsResponse();
			response.Rooms.AddRange(rooms.ToEnumerable());

			return ValueTask.CompletedTask;
		}
	}
}
