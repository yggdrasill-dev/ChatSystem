using Common.Chat;
using Common.Rooms;
using Common.Storage;

namespace Microsoft.Extensions.DependencyInjection;

public static class StorageServiceCollectionExtensions
{
	// **`chat-db` 上的所有東西，一次註冊完。** 房間、封鎖名單、訊息三張表由房間層與聊天層共用
	// （chat-layer.md ADR-4：同一個資料庫、同一套 migration），所以這個註冊不掛在任一層的擴充方法
	// 底下——哪一層先被註冊都不該影響它。
	//
	// **這個組件是唯一認識 EF Core 的地方。** 介面（IRoomStore / IRoomBanList / IChatMessageStore）
	// 與領域型別留在 Common，所以 Gateway、Dispatcher、WebBff 這些不碰資料庫的 process 完全不會
	// 拖到 EF Core——先前它住在 Common 裡的時候，8 個專案會一起撞 EF Core 的版本衝突。
	//
	// 需要先呼叫：
	//   builder.AddNpgsqlDbContext<ChatDbContext>("chat-db")
	//   builder.Services.AddDbContextFactory<ChatDbContext>(o => o.UseNpgsql(...), ServiceLifetime.Singleton)
	//
	// 後者不能省，也不能不帶 optionsAction：三個 store 都是 singleton（其中 ChatRetentionSweeper
	// 還是 BackgroundService），而 DbContext 是 scoped 且非執行緒安全；而 AddDbContextFactory 不給
	// optionsAction 時會註冊一份**沒有 provider** 的 options 蓋掉 Aspire 那份。
	public static IServiceCollection AddChatDb(this IServiceCollection services)
	{
		services.AddSingleton<IRoomStore, PostgresRoomStore>();
		services.AddSingleton<IRoomBanList, PostgresRoomBanList>();
		services.AddSingleton<IChatMessageStore, PostgresChatMessageStore>();

		// **刻意不註冊成 IHostedService**：hosted service 的啟動順序是註冊順序，而這個 process
		// 同一批 hosted service 裡有會立刻開始收訊息的東西（Adaptare 的 queue registration）。
		// migration 必須在那之前確定跑完，所以由宿主在 builder.Build() 之後、host.Run() 之前
		// 明確 await 它。
		return services.AddSingleton<ChatDbMigrator>();
	}
}
