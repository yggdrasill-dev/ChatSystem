using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models;

public class UnregisterSessionCommandService(IMessageQueueService messageQueueService) : ICommandService<UnregisterSessionCommand>
{
	public async ValueTask ExecuteAsync(UnregisterSessionCommand command)
	{
		var loginInfo = new UnregisterRequest
		{
			SessionId = command.SessionId
		};

		await messageQueueService.RequestAsync(
			"session.unregister",
			loginInfo.ToByteArray()).ConfigureAwait(false);
	}
}
