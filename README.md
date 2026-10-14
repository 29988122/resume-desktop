# Resume Desktop｜接續桌面

[English](README.en.md) · [功能與驗證狀態](docs/STATUS.md) · [架構](docs/ARCHITECTURE.md) · [參與開發](CONTRIBUTING.md)

保存 Explorer 視窗、分頁與桌面畫面，在工作中斷後接續原本的工作階段。

**Save Explorer windows, tabs and desktop reference images. Resume your workspace after an interruption.**

## 專案狀態

**0.1.0 預覽版。** 這不是通用的應用程式記憶體備份。Explorer 的復原會回讀驗證；其他應用程式的文件與未儲存內容仍依賴它們自己的恢復功能。

| 功能 | 狀態 |
|---|---|
| 重複路徑、視窗分組、分頁順序、作用中分頁 | 已實作；現機驗證結果見狀態頁 |
| 原子提交、事故前快照、DPAPI 保護、可重試還原 | 已實作 |
| Explorer 位置、大小、最小化／最大化、堆疊 | 已實作；多螢幕及 DPI 差異仍有驗證限制 |
| 本機截圖、應用程式列表、工作列對照 | 已實作；受保護／最小化視窗可能無可用畫面 |
| 工作列左右順序、虛擬桌面分配 | 保存對照，手動調整 |
| Explorer 檢視模式、排序、精確捲動位置 | 尚未還原 |
| 真正 Explorer 當機與 Windows 重開機 | 尚未做破壞性實機測試 |

詳細測試與已知限制以 [STATUS.md](docs/STATUS.md) 為準。只有通過驗證的項目會標示為已驗證。

![Resume Desktop 實際介面，使用合成示範資料](docs/images/demo.png)

## 使用

下載 [Releases](https://github.com/29988122/resume-desktop/releases) 中的 Windows x64 ZIP，解壓縮後執行 `ResumeDesktop.exe`。自帶 .NET 執行環境，不必安裝 SDK。

程式開啟後持續備份。關閉主視窗會縮到通知區；通知區選單的「結束」才停止。從快照紀錄選取事故前狀態，先預覽，按「還原 Explorer」即可補回缺少的視窗與分頁。

手動保護快照快捷鍵為 `Ctrl+Alt+F12`，只在註冊成功時生效；遇到其他程式占用不會強制取代。定期截圖選擇會保留到下次啟動。

- 同一路徑的多個視窗與分頁會各自保留。
- 能完整匹配的現有視窗會復用；額外新開的視窗會保留。
- 不完整或無法辨識的分頁不猜測還原。
- 背景收集不切換分頁、不搶焦點；還原可能讓 Explorer 顯示或移動。
- 程式不會終止或重啟 Explorer、Start Menu 或其他應用程式。
- 登入啟動是通知區選單中的選配功能；只新增目前使用者的啟動項目。

## CLI

```powershell
.\ResumeDesktop.exe probe
.\ResumeDesktop.exe capture --once
.\ResumeDesktop.exe status
.\ResumeDesktop.exe restore --snapshot <id> --explorer --preview
.\ResumeDesktop.exe restore --snapshot <id> --explorer --apply
```

`probe` 預設不輸出私人路徑；`--private-details` 會顯示完整本機資訊。CLI 的預覽與還原報告可能包含路徑，分享前請檢查。

## 資料與隱私

資料只留在 `%LOCALAPPDATA%\ResumeDesktop\`。快照及圖片使用目前 Windows 使用者的 DPAPI 加密，索引僅包含時間、數量與狀態。沒有遙測、雲端上傳或瀏覽器 cookies／密碼擷取。

結構核對約每 5 秒；事件合併後約 750 毫秒觸發；工作列與視窗縮圖約每 15 秒，完整桌面約每 60 秒。helper 逾時會終止該 helper，保留已提交的結構快照。

普通歷史保留最多 7 天，普通圖片預算 2 GiB；保護快照不自動淘汰，可能超過此預算。過期圖片會明確顯示無法讀取。DPAPI 備份應與原 Windows 使用者金鑰一起保存，不能直接當成跨電腦可攜式備份。

## 從原始碼建置

Windows 11 x64、.NET SDK 10.0.402。沒有第三方 NuGet 相依套件。

```powershell
dotnet build src/ResumeDesktop.App -c Release
dotnet run --project tests/ResumeDesktop.Tests -c Release
dotnet run --project src/ResumeDesktop.App -- probe
dotnet publish src/ResumeDesktop.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

原生互動測試會新開、還原及關閉**測試專用**的 Explorer 視窗，不終止 Explorer。執行方式見 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 授權與參與

[MIT](LICENSE)。歡迎提交可重現的相容性問題、測試及修正；維護狀態以 repository 的實際活動為準，不預先保證有人持續維護。引用來源列於 [第三方聲明](THIRD_PARTY_NOTICES.md)。
