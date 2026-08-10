// **本組測試不能並行，理由是 Adaptare.Direct 的 queue 是 process 範圍的。**
//
// CommandFlowHost 自己的註解早就寫著「一個 process 裡只有一個 Direct queue」——那句話原本是
// 用來說明「跨節點 fan-out 驗不到」，但它還有第二個後果沒被寫下來：**兩個 host 同時活著的時候
// 會互相偷訊息。** xunit 預設把每個測試類別當成一個 collection、不同 collection 並行執行，而
// ChatCommandFlowTests / RoomCommandFlowTests / DirectMessagingProbeTests 三個類別都會建 host，
// 於是 A 送出的 command.inbound 可能被 B 的 processor 收走——B 的 store 裡沒有那間房、也沒有那條
// 連線，所以什麼都不會發生，而 A 看到的是「回覆完全沒到」。
//
// 症狀是整組隨機紅一到數條，錯誤一律是 Assert.Single() Failure: The collection was empty，
// 落在 CreateRoomAsync 讀第一則回覆的地方。**單獨跑任何一個類別永遠不會紅**，所以它很容易被
// 當成某一條測試自己的時序問題——實際上跟被指控的那條測試無關。
//
// 序列化的代價是零：整組 0.6 秒跑完。真正的替代方案（給每個 host 一組獨立的 subject 前綴）
// 會讓 harness 為了測試基礎設施而改變被測對象的 subject，那個代價比省下的並行度大。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
