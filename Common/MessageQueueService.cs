using System;
using System.Threading.Tasks;
using NATS.Client;

namespace Common
{
	internal class MessageQueueService : IMessageQueueService
	{
		private readonly IConnection m_Connection;

		public MessageQueueService(ConnectionFactory connectionFactory)
		{
			m_Connection = connectionFactory.CreateConnection();
		}

		public ValueTask PublishAsync(string subject, byte[] data)
		{
			m_Connection.Publish(subject, data);

			return ValueTask.CompletedTask;
		}

		public async ValueTask<Msg> RequestAsync(string subject, byte[] data)
		{
			return await m_Connection.RequestAsync(subject, data).ConfigureAwait(false);
		}

		public ValueTask<IDisposable> SubscribeAsync(string subject, EventHandler<MsgHandlerEventArgs> eventHandler)
		{
			var subscription = m_Connection.SubscribeAsync(subject, eventHandler);

			return new ValueTask<IDisposable>(subscription);
		}

		public ValueTask<IDisposable> SubscribeAsync(string subject, string queue, EventHandler<MsgHandlerEventArgs> eventHandler)
		{
			var subscription = m_Connection.SubscribeAsync(subject, queue, eventHandler);

			return new ValueTask<IDisposable>(subscription);
		}
	}
}
