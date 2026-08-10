using Common.Rooms;

namespace Common.Tests.Rooms;

// 契約跑在 in-memory 實作上：不需要容器、跟其他單元測試一起 0.4 秒跑完。
// 同一組斷言在 E2E.Tests 也跑一次，那邊接的是 Postgres（PostgresRoomStoreContractTests）。
public sealed class InMemoryRoomStoreContractTests : RoomStoreContract
{
	protected override ValueTask<IRoomStore> NewStoreAsync() =>
		ValueTask.FromResult<IRoomStore>(new InMemoryRoomStore());
}

public sealed class InMemoryRoomBanListContractTests : RoomBanListContract
{
	protected override ValueTask<(IRoomStore Store, IRoomBanList Bans)> NewAsync() =>
		ValueTask.FromResult<(IRoomStore, IRoomBanList)>((new InMemoryRoomStore(), new InMemoryRoomBanList()));
}
