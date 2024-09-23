using System;
using System.Threading.Tasks;
using NATS.Client;

namespace Common;

public interface IMessageQueueService
{
	ValueTask PublishAsync(string subject, byte[] data);
	ValueTask<Msg> RequestAsync(string subject, byte[] data);
	ValueTask<IDisposable> SubscribeAsync(string subject, EventHandler<MsgHandlerEventArgs> eventHandler);
	ValueTask<IDisposable> SubscribeAsync(string subject, string queue, EventHandler<MsgHandlerEventArgs> eventHandler);
}