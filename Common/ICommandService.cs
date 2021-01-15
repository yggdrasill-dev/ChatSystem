using System.Threading.Tasks;

namespace Common
{
	public interface ICommandService<TCommand>
	{
		ValueTask ExecuteAsync(TCommand command);
	}
}
