using System.Threading;
using System.Threading.Tasks;
using NATS.Client;

namespace Common;

public interface IMessageHandler
{
	ValueTask HandleAsync(Msg msg, CancellationToken cancellationToken);
}
