using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Quartz;

namespace ChatConnector.Models;

public class ActiveSessionsJob(
	WebSocketRepository socketRepository,
	IMessageQueueService messageQueueService) : IJob
{
	public Task Execute(IJobExecutionContext context)
	{
		var allSessionIds = socketRepository.Keys;

		var sendData = new ActiveSessionsRequest();
		sendData.SessionIds.AddRange(allSessionIds);

		return messageQueueService.PublishAsync("session.active", sendData.ToByteArray()).AsTask();
	}
}
