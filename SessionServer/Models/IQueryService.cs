using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SessionServer.Models
{
	public interface IQueryService<TQuery, TResult>
	{
		IAsyncEnumerable<TResult> QueryAsync(TQuery query);
	}
}
