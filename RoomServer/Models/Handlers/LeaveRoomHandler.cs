using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Handlers
{
	public class LeaveRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<LeaveRoomCommand> m_LeaveRoomService;
		private readonly IQueryService<RoomSessionsQuery, string> m_ListRoomSessionsService;
		private readonly ILogger<LeaveRoomHandler> m_Logger;

		public LeaveRoomHandler(
			IMessageQueueService messageQueueService,
			ICommandService<LeaveRoomCommand> leaveRoomService,
			IQueryService<RoomSessionsQuery, string> listRoomSessionsService,
			ILogger<LeaveRoomHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_LeaveRoomService = leaveRoomService ?? throw new ArgumentNullException(nameof(leaveRoomService));
			m_ListRoomSessionsService = listRoomSessionsService ?? throw new ArgumentNullException(nameof(listRoomSessionsService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var request = LeaveRoomRequest.Parser.ParseFrom(msg.Data);

			await m_LeaveRoomService.ExecuteAsync(new LeaveRoomCommand
			{
				SessionId = request.SessionId,
				Room = request.Room
			}).ConfigureAwait(false);

			var roomSessionIds = m_ListRoomSessionsService
				.QueryAsync(new RoomSessionsQuery
				{
					Room = request.Room
				})
				.ToEnumerable();

			var refrashPacket = new SendPacket
			{
				Subject = "room.player.refrash"
			};

			refrashPacket.SessionIds.AddRange(roomSessionIds);

			await m_MessageQueueService.PublishAsync(
				"connect.send",
				refrashPacket.ToByteArray()).ConfigureAwait(false);

			await m_MessageQueueService.PublishAsync(msg.Reply, null).ConfigureAwait(false);

			m_Logger.LogInformation($"({request.SessionId}, {request.Room}) leaved.");
		}
	}
}
