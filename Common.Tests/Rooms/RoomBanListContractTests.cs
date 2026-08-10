using Common.Rooms;

namespace Common.Tests.Rooms;

// **這些是 IRoomBanList 的契約，不是替身的規格。** 階段 B 的 PostgresRoomBanList 必須通過同一組
// 斷言——屆時把 NewBanList() 換掉就行。
//
// ADR-10 不會動到這一份：封鎖名單的三個操作跟「房間關了還是刪了」無關。刪房那一刀是
// ON DELETE CASCADE 在 rooms 上發生的事，不經過這個介面。
public class RoomBanListContractTests
{
	[Fact]
	public async Task IsBanned_ReturnsFalse_ForSomeoneWhoWasNeverBanned() =>
		Assert.False(await NewBanList().IsBannedAsync("room-1", "user-1"));

	[Fact]
	public async Task Ban_MakesTheUserBanned()
	{
		var banList = NewBanList();

		await banList.BanAsync("room-1", "user-1");

		Assert.True(await banList.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Ban_IsIdempotent()
	{
		var banList = NewBanList();

		// 介面刻意沒有 Try 語意，理由是「重複封鎖同一個人跟封鎖一次的結果一樣」。那句話在
		// Postgres 上要靠 INSERT ... ON CONFLICT DO NOTHING 才成立——少寫就是 23505。
		await banList.BanAsync("room-1", "user-1");
		await banList.BanAsync("room-1", "user-1");

		Assert.True(await banList.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Unban_RemovesTheBan()
	{
		var banList = NewBanList();
		await banList.BanAsync("room-1", "user-1");

		await banList.UnbanAsync("room-1", "user-1");

		Assert.False(await banList.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Unban_IsIdempotent_WhenTheUserWasNotBanned()
	{
		// 解鎖一個沒被封鎖的人不是錯誤，不丟例外。後台 UI 上「踢出＋封鎖」與「解鎖」是兩個
		// 獨立按鈕，重複點擊到得了這裡。
		await NewBanList().UnbanAsync("room-1", "user-1");

		Assert.False(await NewBanList().IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Bans_AreScopedToOneRoom()
	{
		var banList = NewBanList();

		await banList.BanAsync("room-1", "user-1");

		// 主鍵是 (room_id, user_id)，不是 user_id 單獨——封鎖是房間的後台動作，不是全站黑名單。
		Assert.False(await banList.IsBannedAsync("room-2", "user-1"));
	}

	private static IRoomBanList NewBanList() => new InMemoryRoomBanList();
}
