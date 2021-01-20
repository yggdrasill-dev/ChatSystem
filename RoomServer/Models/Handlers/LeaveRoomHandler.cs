using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models.Handlers
{
	public class LeaveRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<LeaveRoomCommand> m_LeaveRoomService;
		private readonly ILogger<LeaveRoomHandler> m_Logger;

		public LeaveRoomHandler(
			IMessageQueueService messageQueueService,
			ICommandService<LeaveRoomCommand> leaveRoomService,
			ILogger<LeaveRoomHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_LeaveRoomService = leaveRoomService ?? throw new ArgumentNullException(nameof(leaveRoomService));
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

			await m_MessageQueueService.PublishAsync(msg.Reply, null).ConfigureAwait(false);

			m_Logger.LogInformation($"({request.SessionId}, {request.Room}) leaved.");
		}
	}
}
