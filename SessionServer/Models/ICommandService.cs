using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SessionServer.Models
{
	interface ICommandService<TCommand>
	{
		ValueTask ExecuteAsync(TCommand command);
	}
}
