# 功能與驗證狀態 / Implementation and Verification Status

Version: **0.1.0 preview**

States: **已實作且已驗證 / implemented and verified**, **已實作但待驗證 / implemented, validation pending**, **部分支援 / partial**, **尚未實作 / not implemented**.

## Verified environment

- Windows 11 Education 25H2, build 26200.
- Explorer 10.0.26100.8875.
- .NET SDK 10.0.402, x64.
- Core checks: **27/27 passed**.
- Read-only live Explorer probe: **passed**.
- Native smoke: **passed**, two windows / four tabs, duplicate paths, active-tab restoration, repeated recovery, genuine close-and-reopen recovery, Explorer relative stacking and preservation of existing Explorer windows.
- Current-user DPAPI and screenshot capture: **passed** during local capture and native recovery.

## Capability matrix

| Capability | Implementation | Verification |
|---|---|---|
| Duplicate-window and duplicate-tab preservation | Implemented and verified | Core tests and native two-window/four-tab smoke passed |
| Active-tab and ordered-sequence matching | Implemented and verified | Core tests and native smoke passed |
| Partial retry ledger / modified target preservation | Implemented | Core tests passed |
| Atomic commit / corrupt-index handling | Implemented | Core tests passed |
| Pre-loss / protected / last-nonempty history | Implemented | Core tests passed |
| Current-user DPAPI | Implemented and verified | Encrypted native snapshot and ledger readback passed |
| Window creation and navigation | Implemented and verified | Native close-and-reopen smoke passed |
| Placement and stacking | Partial | Explorer relative stacking passed; other-app-relative order and multi-monitor/DPI remain limited |
| Screenshots, thumbnails and taskbar reference | Partial | Some apps can return black/stale images; minimized windows skipped |
| Virtual desktop grouping | Partial | GUID capture only; empty desktops/names not enumerated; assignment manual |
| Startup / long-running recorder | Implemented | Extended soak and login testing pending |
| View mode / sorting / exact scroll offset | Not implemented | Explicitly excluded from preview recovery |
| Arbitrary app internals / unsaved documents | Not implemented | Requires app-specific mechanisms |
| Actual Explorer process crash / Windows reboot | Validation pending | Not performed; no shell process was killed |

## Known limits

- Explorer tab control has no complete stable public API. The new-tab message is version-sensitive.
- Same visible labels for different folder locations may prevent reliable passive tab mapping.
- Recovery keeps unrelated live Explorer windows. The total count can exceed the source snapshot, by design.
- A decrease in Explorer window count is treated as a possible loss, not proof of a crash. Recovery is manual.
- All reconstructed windows are created on the current virtual desktop.
- UI Automation enumeration and desktop thumbnails are reference data, not a complete replica of Windows Task View or Alt-Tab history.
- View properties and precise scroll position are not recovered.
- Cross-monitor DPI changes use visible-work-area clamping, not a proven equivalent layout.
- Windows focus rules can require activating the top restored Explorer to reproduce relative stacking. Placement relative to other applications can differ.
- Protected snapshots can grow beyond the ordinary history/image budget.
- The UI defaults to Traditional Chinese; documentation is bilingual.

Update this document when verification changes. Never promote a pending item based solely on compilation or a mocked test.
