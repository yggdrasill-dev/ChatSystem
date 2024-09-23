using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace Common;

internal class MessageQueueService(
	IConnection connection) : IMessageQueueService
{
	public ValueTask PublishAsync(string subject, byte[] data)
	{
		connection.Publish(subject, data);

		return ValueTask.CompletedTask;
	}

	public async ValueTask<Msg> RequestAsync(string subject, byte[] data)
	{
		return await connection.RequestAsync(subject, data).ConfigureAwait(false);
	}

	public ValueTask<IDisposable> SubscribeAsync(string subject, EventHandler<MsgHandlerEventArgs> eventHandler)
	{
		var subscription = connection.SubscribeAsync(subject, eventHandler);

		return new ValueTask<IDisposable>(subscription);
	}

	public ValueTask<IDisposable> SubscribeAsync(string subject, string queue, EventHandler<MsgHandlerEventArgs> eventHandler)
	{
		var subscription = connection.SubscribeAsync(subject, queue, eventHandler);

		return new ValueTask<IDisposable>(subscription);
	}
}
