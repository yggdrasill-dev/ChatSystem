using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SessionServer.Models
{
	public interface IGetService<TQuery, TResult>
	{
		ValueTask<TResult> GetAsync(TQuery query);
	}
}