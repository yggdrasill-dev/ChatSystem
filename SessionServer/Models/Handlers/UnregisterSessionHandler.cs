using System.Threading;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Microsoft.Extensions.Logging;
using NATS.Client;

namespace SessionServer.Models.Handlers;

public class UnregisterSessionHandler(
	IMessageQueueService messageQueueService,
	ICommandService<UnregisterSessionCommand> unregisterSessionService,
	ILogger<UnregisterSessionHandler> logger) : IMessageHandler
{
	public async ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken)
	{
		var registration = UnregisterRequest.Parser.ParseFrom(msg.Data);

		await unregisterSessionService.ExecuteAsync(new UnregisterSessionCommand
		{
			SessionId = registration.SessionId
		}).ConfigureAwait(false);

		await messageQueueService.PublishAsync(msg.Reply, []).ConfigureAwait(false);

		logger.LogInformation("({SessionId}) unregistered.", registration.SessionId);
	}
}
