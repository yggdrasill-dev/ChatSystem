using System;
using System.Linq;
using System.Threading.Tasks;
using Chat.Protos;
using Common;
using Google.Protobuf;
using Quartz;

namespace ChatConnector.Models
{
	public class ActiveSessionsJob : IJob
	{
		private readonly WebSocketRepository m_SocketRepository;
		private readonly IMessageQueueService m_MessageQueueService;

		public ActiveSessionsJob(WebSocketRepository socketRepository, IMessageQueueService messageQueueService)
		{
			m_SocketRepository = socketRepository ?? throw new ArgumentNullException(nameof(socketRepository));
			m_MessageQueueService = messageQueueService ?? throw new ArgumentNullException(nameof(messageQueueService));
		}

		public Task Execute(IJobExecutionContext context)
		{
			var allSessionIds = m_SocketRepository.Keys;

			var sendData = new ActiveSessionsRequest();
			sendData.SessionIds.AddRange(allSessionIds);

			return m_MessageQueueService.PublishAsync("session.active", sendData.ToByteArray()).AsTask();
		}
	}
}
