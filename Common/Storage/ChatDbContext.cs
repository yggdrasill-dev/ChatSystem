using Common.Chat;
using Common.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Common.Storage;

// chat-db 的 schema，房間層與聊天層共用（chat-layer.md ADR-4：同一個資料庫、同一套 migration）。
// 所以它住在 Common/Storage 而不是任一層底下——它不屬於其中任何一層。
//
// **領域型別上沒有任何 EF 的痕跡**：`Room` 與 `ChatMessage` 沒有 attribute、沒有 navigation
// property、沒有為了 EF 加的無參數建構子。對映全部在這裡用 fluent API 設定，兩個 record 因此
// 仍然是「一個房間 / 一則訊息」而不是「一列資料」。這是刻意的——ADR-4 說換掉儲存的成本是一次
// 資料遷移，那句話只有在領域型別不認識儲存時才是真的。
public sealed class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
	public DbSet<Room> Rooms => Set<Room>();

	public DbSet<RoomBan> RoomBans => Set<RoomBan>();

	public DbSet<ChatMessage> Messages => Set<ChatMessage>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// **欄位名一律顯式寫出來**，不引入 EFCore.NamingConventions 去自動轉 snake_case。
		// 11 個欄位手寫得完，而那個套件是整個 model 的全域行為——跟「不打開 Dapper 的
		// MatchNamesWithUnderscores、不註冊全域 TypeHandler」是同一條理由。
		modelBuilder.Entity<Room>(entity =>
		{
			entity.ToTable("rooms");
			entity.HasKey(room => room.RoomId);

			entity.Property(room => room.RoomId).HasColumnName("room_id");
			entity.Property(room => room.Name).HasColumnName("name").IsRequired();
			// NULL = 公開房。`room.list` 的 has_password 直接看它，所以「公開房存的是 NULL
			// 而不是空字串」是契約測試盯著的一條。
			entity.Property(room => room.PasswordHash).HasColumnName("password_hash");
			entity.Property(room => room.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
			entity.Property(room => room.CreatedAt).HasColumnName("created_at");
		});

		modelBuilder.Entity<RoomBan>(entity =>
		{
			entity.ToTable("room_bans");
			entity.HasKey(ban => new { ban.RoomId, ban.UserId });

			entity.Property(ban => ban.RoomId).HasColumnName("room_id");
			entity.Property(ban => ban.UserId).HasColumnName("user_id");

			// **FK 沒有 navigation property**：`HasOne<Room>().WithMany()` 建得起 FK 而不用在
			// Room 上掛一個 ICollection<RoomBan>。領域模型裡沒有「房間持有它的封鎖名單」這個概念
			// ——那是 IRoomBanList 的職責，加一個 navigation 只會多一條沒人走的路。
			//
			// Cascade 是 ADR-10 的實作：刪掉一列 rooms 就把封鎖名單與訊息一起帶走。
			entity.HasOne<Room>()
				.WithMany()
				.HasForeignKey(ban => ban.RoomId)
				.OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<ChatMessage>(entity =>
		{
			entity.ToTable("messages");

			// 主鍵直接服務唯一的讀取路徑（keyset 分頁），不需要任何額外索引。**排序鍵稀疏對
			// B-tree 完全沒有影響**——bigint 不管值多大都是 8 bytes（ADR-2）。
			entity.HasKey(message => new { message.RoomId, message.OrderKey });

			entity.Property(message => message.RoomId).HasColumnName("room_id");
			entity.Property(message => message.OrderKey).HasColumnName("order_key");
			entity.Property(message => message.SenderUserId).HasColumnName("sender_user_id").IsRequired();
			entity.Property(message => message.SenderDisplayName)
				.HasColumnName("sender_display_name")
				.IsRequired();
			entity.Property(message => message.Body).HasColumnName("body").IsRequired();
			entity.Property(message => message.SentAt).HasColumnName("sent_at");

			// 只給保留期限的清理用（ADR-6）。讀取路徑不需要它。
			entity.HasIndex(message => message.SentAt).HasDatabaseName("messages_sent_at_idx");

			entity.HasOne<Room>()
				.WithMany()
				.HasForeignKey(message => message.RoomId)
				.OnDelete(DeleteBehavior.Cascade);
		});
	}
}

// **只為儲存存在的型別**：封鎖名單在領域裡是 `IRoomBanList` 的三個方法（是不是、封鎖、解鎖），
// 沒有一個「RoomBan」的概念需要被傳來傳去。但 EF 要一個 entity 才對映得了那張表，所以它待在
// Common/Storage 而不是 Common/Rooms——放到後者會讓人以為領域多了一個型別。
//
// `Room` 與 `ChatMessage` 沒有這個問題：它們本來就是領域型別，EF 只是拿來對映。
public sealed class RoomBan
{
	public required string RoomId { get; init; }

	public required string UserId { get; init; }
}
