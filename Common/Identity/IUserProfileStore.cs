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
}
