using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace SessionServer.Models
{
	public class UnregisterSessionHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<UnregisterSessionCommand> m_UnregisterSessionService;
		private readonly ILogger<UnregisterSessionHandler> m_Logger;

		public UnregisterSessionHandler(
			IMessageQueueService messageQueueService,
			ICommandService<UnregisterSessionCommand> unregisterSessionService,
			ILogger<UnregisterSessionHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_UnregisterSessionService = unregisterSessionService ?? throw new ArgumentNullException(nameof(unregisterSessionService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var registration = UnregisterRequest.Parser.ParseFrom(msg.Data);

			await m_UnregisterSessionService.ExecuteAsync(new UnregisterSessionCommand
			{
				SessionId = registration.SessionId
			}).ConfigureAwait(false);

			await m_MessageQueueService.PublishAsync(msg.Reply, null).ConfigureAwait(false);

			m_Logger.LogInformation($"({registration.SessionId}) unregistered.");
		}
	}
}
