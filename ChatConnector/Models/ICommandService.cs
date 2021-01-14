using System.Threading.Tasks;

namespace ChatConnector.Models
{
	public interface ICommandService<TCommand>
	{
		ValueTask ExecuteAsync(TCommand command);
	}
}
