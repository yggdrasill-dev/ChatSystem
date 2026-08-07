namespace Common.Protocol;

// 一則命令的脈絡：哪條連線送的、以及那條連線在 handshake 驗證通過的 principal。
//
// Principal 是不透明字串，協定層不解讀也不驗證它——值由 Gateway 填在 InboundPacket 上，
// 隨封包一起到，所以本層不需要任何 connectionId -> 身分的對照表。
//
// 用 struct 包起來而不是兩個平行參數：目前只有兩個欄位、確實還不需要，但事後往
// IPacketHandler 加參數是破壞性變更（會動到每個 handler 與每個測試），
// 而可預期會想放進來的東西不少（trace id、收到訊息的時間戳、metrics 標籤）。
// 這跟「一開始就 per-command 開 DI scope」是同一個判斷。
public readonly record struct CommandContext(string ConnectionId, string Principal);
