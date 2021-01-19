namespace SessionServer.Models
{
	public struct RegisterCommand
	{
		public string SessionId { get; set; }

		public string ConnectorId { get; set; }

		public string Name { get; set; }
	}
}
