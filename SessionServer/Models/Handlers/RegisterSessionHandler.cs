using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace SessionServer.Models.Handlers;

public class RegisterSessionHandler(
	IMessageQueueService messageQueueService,
	ICommandService<RegisterCommand> registerService,
	ILogger<RegisterSessionHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var registration = RegisterRequest.Parser.ParseFrom(msg.Data);

		await registerService.ExecuteAsync(new RegisterCommand
		{
			SessionId = registration.SessionId,
			ConnectorId = registration.ConnectorId,
			Name = registration.Name
		}).ConfigureAwait(false);

		await messageQueueService.PublishAsync(msg.Reply, []);

		logger.LogInformation(
			"({SessionId}, {Name}) registered.",
			registration.SessionId,
			registration.Name);
	}
}
