using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models;

public class RegisterSessionCommandService(IMessageQueueService messageQueueService) : ICommandService<RegisterSessionCommand>
{
	public async ValueTask ExecuteAsync(RegisterSessionCommand command)
	{
		var loginInfo = new RegisterRequest
		{
			SessionId = command.SessionId,
			ConnectorId = command.ConnectorId,
			Name = command.Name
		};

		await messageQueueService.RequestAsync(
			"session.register",
			loginInfo.ToByteArray()).ConfigureAwait(false);
	}
}
