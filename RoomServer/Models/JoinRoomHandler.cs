using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace RoomServer.Models
{
	public class JoinRoomHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<JoinRoomCommand> m_JoinRoomService;
		private readonly ILogger<JoinRoomHandler> m_Logger;

		public JoinRoomHandler(
			IMessageQueueService messageQueueService,
			ICommandService<JoinRoomCommand> joinRoomService,
			ILogger<JoinRoomHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_JoinRoomService = joinRoomService ?? throw new ArgumentNullException(nameof(joinRoomService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var request = JoinRoomRequest.Parser.ParseFrom(msg.Data);

			await m_JoinRoomService.ExecuteAsync(new JoinRoomCommand
			{
				SessionId = request.SessionId,
				Room = request.Room
			}).ConfigureAwait(false);

			await m_MessageQueueService.PublishAsync(msg.Reply, null).ConfigureAwait(false);

			m_Logger.LogInformation($"({request.SessionId}, {request.Room}) joined.");
		}
	}
}
