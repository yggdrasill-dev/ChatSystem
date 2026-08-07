namespace Common.Identity;

// 使用者的顯示資料（暱稱、頭像網址）。
//
// 存在的理由是時機問題：`sub` 是一串數字，UI 上不能看，而 Google ID Token 是整個系統
// **唯一**看得到 name / picture 的地方。登入的那一刻不寫下來，之後要補只能叫所有人重新登入。
public interface IUserProfileStore
{
	ValueTask SaveAsync(
		string userId,
		string? displayName,
		string? pictureUrl,
		CancellationToken cancellationToken = default);

	// 聊天層要把**送出當下的**顯示名稱存進訊息（chat-layer.md ADR-3），所以需要單筆讀取。
	//
	// 這只回答了 identity-layer.md ADR-11 的一半：那條 ADR 說查詢介面「等房間層真的要顯示
	// 成員名單時再設計，屆時才知道需要的是單筆還是批次」。答案是**兩個都要**——
	// 批次讀 N 個成員的 profile 仍然沒有介面，那是 RoomJoined.member_user_ids 要在 UI 上顯示
	// 成名字時才會逼出來的，屬於 webClient 的需求。
	//
	// null = 這個使用者沒有 profile（沒登入過或資料被清掉）；空字串 = 登入時 Google 沒給
	// name。兩者對聊天層是同一個結果（快照留空，顯示什麼由 client 決定），但介面上分得出來。
	ValueTask<string?> GetDisplayNameAsync(string userId, CancellationToken cancellationToken = default);
}
