using Common.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class StorageServiceCollectionExtensions
{
	// chat-db 的 schema 由房間層與聊天層共用（chat-layer.md ADR-4），所以 migrator 的註冊不掛在
	// 任何一層的擴充方法底下——哪一層先被註冊都不該影響它。
	//
	// 需要先呼叫 builder.AddNpgsqlDbContext<ChatDbContext>("chat-db")。**刻意不註冊成 IHostedService**：
	// hosted service 的啟動順序是註冊順序，而這個 process 同一批 hosted service 裡有會立刻開始
	// 收訊息的東西（Adaptare 的 queue registration）。migration 必須在那之前確定跑完，所以由
	// 宿主在 builder.Build() 之後、host.Run() 之前明確 await 它。
	public static IServiceCollection AddChatDb(this IServiceCollection services) =>
		services.AddSingleton<ChatDbMigrator>();
}
