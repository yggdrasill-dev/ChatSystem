using Common.Rooms;

namespace Common.Tests.Rooms;

// **這些是 IRoomBanList 的契約，不是替身的規格。** 抽象基底，每個實作派生一個殼去跑同一組斷言
// （形狀見 RoomStoreContract）。
//
// **B2 才發現的一條前置條件：房間必須存在。** Postgres 版的 `room_bans.room_id` 有 FK 指向
// `rooms`（那是 ADR-10 的 CASCADE 機制），對不存在的房間下封鎖會丟 23503；Redis 的 Set 從來
// 不管這件事，所以 B0 那版契約測試直接對「room-1」下封鎖、沒建房間也過得去。那條前置條件
// **一直都成立**——`RoomBanHandler` 先讀房間、檢查房主才動作——只是沒被寫下來，於是換儲存的
// 時候才被 FK 逼出來。現在每條測試都先建房間，因為那才是真實的呼叫順序。
//
// ADR-10 那條「刪房把封鎖名單一起帶走」不在這裡：它在每個實作上長得不一樣（Postgres 是
// ON DELETE CASCADE、Redis 是 TryDeleteAsync 裡多刪一個 key），而 in-memory 的兩個替身是獨立
// 物件、湊不出那個連動。由各實作自己的測試守。
public abstract class RoomBanListContract
{
	[Fact]
	public async Task IsBanned_ReturnsFalse_ForSomeoneWhoWasNeverBanned()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1");

		Assert.False(await bans.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Ban_MakesTheUserBanned()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1");

		await bans.BanAsync("room-1", "user-1");

		Assert.True(await bans.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Ban_IsIdempotent()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1");

		// 介面刻意沒有 Try 語意，理由是「重複封鎖同一個人跟封鎖一次的結果一樣」。那句話在
		// Postgres 上要靠 INSERT ... ON CONFLICT DO NOTHING 才成立——少寫就是 23505。
		await bans.BanAsync("room-1", "user-1");
		await bans.BanAsync("room-1", "user-1");

		Assert.True(await bans.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Unban_RemovesTheBan()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1");
		await bans.BanAsync("room-1", "user-1");

		await bans.UnbanAsync("room-1", "user-1");

		Assert.False(await bans.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Unban_IsIdempotent_WhenTheUserWasNotBanned()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1");

		// 解鎖一個沒被封鎖的人不是錯誤，不丟例外（Postgres 上就是影響 0 列）。後台 UI 上
		// 「踢出＋封鎖」與「解鎖」是兩個獨立按鈕，重複點擊到得了這裡。
		await bans.UnbanAsync("room-1", "user-1");

		Assert.False(await bans.IsBannedAsync("room-1", "user-1"));
	}

	[Fact]
	public async Task Bans_AreScopedToOneRoom()
	{
		var (_, bans) = await NewWithRoomsAsync("room-1", "room-2");

		await bans.BanAsync("room-1", "user-1");

		// 主鍵是 (room_id, user_id)，不是 user_id 單獨——封鎖是房間的後台動作，不是全站黑名單。
		Assert.False(await bans.IsBannedAsync("room-2", "user-1"));
	}

	// 實作提供一組**空的** store + 封鎖名單。兩個一起給是因為封鎖名單的前置條件是「房間存在」，
	// 而建房間只能經由 IRoomStore。
	protected abstract ValueTask<(IRoomStore Store, IRoomBanList Bans)> NewAsync();

	private async Task<(IRoomStore Store, IRoomBanList Bans)> NewWithRoomsAsync(params string[] roomIds)
	{
		var pair = await NewAsync();

		foreach (var roomId in roomIds)
		{
			Assert.True(await pair.Store.TryCreateAsync(
				new Room(
					roomId,
					"Lobby",
					null,
					"owner",
					DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000))));
		}

		return pair;
	}
}
