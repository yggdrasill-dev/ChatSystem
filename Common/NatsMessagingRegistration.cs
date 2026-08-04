namespace Common;

// 這個檔案原本有一個 AddNatsMessaging() 擴充方法，把 Adaptare 的共用設定
// （AddMessageQueue + AddNatsMessageQueue + AddNatsGlobPatternExchange）包起來，並用一個
// marker 確保「只跑一次」。它被 AddOutboundGateway / AddConnectionTerminator /
// AddConnectionEventPublisher / AddInboundBridge 各自呼叫。
//
// **那個設計是一個 bug 的來源，已經移除。** marker 只擋得住我們自己的重複呼叫，擋不住
// 「應用程式為了註冊自己的 handler 又呼叫一次 AddNatsMessageQueue」——而 Gateway、
// Dispatcher、CommandRouter 三個都是這樣。每多一次 AddNatsMessageQueue 就多一個
// IMessageQueueBackgroundRegistration，而 handler 的設定是共用的 options，所以**每個訂閱
// 被建立兩份、每則下行訊息被投遞兩次**。
//
// 這個 bug 撐過了兩次端到端驗證：在房間層出現之前，下行只有 terminate（重複關同一條連線
// 是 no-op）與 ack（request/reply 只取第一個回覆），所以重複完全看不出來。
//
// 現在的規則是：**每個 host 在自己的 Program.cs 裡明確組一次完整的鏈**，共用設定那兩行
// 因此在四個 Program.cs 裡各出現一次。這是刻意付的重複代價——換來「不可能註冊兩次」這件事
// 在每個 Program.cs 裡看得見，而不是藏在一個 marker 後面。
// 回歸測試見 Common.Tests/Protocol/NatsMessagingRegistrationTests.cs。
