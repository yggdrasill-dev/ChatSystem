using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Common.Storage;

// 只給 `dotnet ef` 用。**存在的理由是讓 migration 是 Common 自己的事**——沒有它的話
// `dotnet ef migrations add` 要指定 --startup-project CommandRouter，於是「產生一個 migration」
// 會需要那個 process 的整份 DI（NATS、Redis、Aspire 的連線字串）都解析得起來。
//
// 這裡的連線字串是**假的、不會被連上**：產生 migration 只需要 model，不需要資料庫。真正的連線
// 字串由 Aspire 在執行時注入（CommandRouter/Program.cs 的 AddNpgsqlDbContext）。
internal sealed class ChatDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
	public ChatDbContext CreateDbContext(string[] args) =>
		new(new DbContextOptionsBuilder<ChatDbContext>()
			.UseNpgsql("Host=design-time-only;Database=chat-db")
			.Options);
}
