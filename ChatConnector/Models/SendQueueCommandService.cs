using System;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;

namespace ChatConnector.Models;

public class SendQueueCommandService(IMessageQueueService messageQueueService) : ICommandService<SendQueueCommand>
{
	public ValueTask ExecuteAsync(SendQueueCommand command)
	{
		var packet = new QueuePacket
		{
			SessionId = command.SessionId,
			Payload = command.Payload
		};

		return messageQueueService.PublishAsync(command.Subject, packet.ToByteArray());
	}
}
