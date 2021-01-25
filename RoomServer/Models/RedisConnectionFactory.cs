using StackExchange.Redis;

namespace RoomServer.Models
{
	public class RedisConnectionFactory
	{
		public IDatabase Database { get; }

		public IServer Server { get; }

		public RedisConnectionFactory(string connectionString)
		{
			var connection = ConnectionMultiplexer.Connect(connectionString);

			Database = connection.GetDatabase();

			Server = connection.GetServer(connectionString);
		}
	}
}
