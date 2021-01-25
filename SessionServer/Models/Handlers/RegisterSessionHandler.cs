using System;
using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace SessionServer.Models.Handlers
{
	public class RegisterSessionHandler : IMessageHandler
	{
		private readonly IMessageQueueService m_MessageQueueService;
		private readonly ICommandService<RegisterCommand> m_RegisterService;
		private readonly ILogger<RegisterSessionHandler> m_Logger;

		public RegisterSessionHandler(
			IMessageQueueService messageQueueService,
			ICommandService<RegisterCommand> registerService,
			ILogger<RegisterSessionHandler> logger)
		{
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
			m_RegisterService = registerService ?? throw new ArgumentNullException(nameof(registerService));
			m_Logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
		{
			var registration = RegisterRequest.Parser.ParseFrom(msg.Data);

			await m_RegisterService.ExecuteAsync(new RegisterCommand
			{
				SessionId = registration.SessionId,
				ConnectorId = registration.ConnectorId,
				Name = registration.Name
			}).ConfigureAwait(false);

			await m_MessageQueueService.PublishAsync(msg.Reply, Array.Empty<byte>());

			m_Logger.LogInformation($"({registration.SessionId}, {registration.Name}) registered.");
		}
	}
}
