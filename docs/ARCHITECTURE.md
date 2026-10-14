# Architecture

The solution has three layers: a platform-independent core, a Windows adapter, and a WPF/CLI host.

## Identity and capture

Win32 top-level handles establish window grouping and stacking. Shell COM maps each folder view to a `ShellTabWindowClass` handle and reads its parsing location. UI Automation supplies the folder-tab order and active selection. Classic ribbon tabs are excluded.

Handles and process ids are qualified by process start time. They are never assumed valid across a reboot. Snapshot ids are random identifiers. Window and tab collections preserve duplicates.

Identical visible tab labels with different locations can be ambiguous. Such windows are marked incomplete rather than ordered by native child enumeration. One-tab windows do not require a folder tab strip.

The recorder uses out-of-context WinEvent hooks and polling. Collection is executed in a separate STA helper. A helper can be terminated on timeout without terminating Explorer. Structure is committed before screenshot work.

## Durable storage

A snapshot body is encrypted with current-user DPAPI and atomically renamed into place. A non-sensitive metadata index is committed last; incomplete writes are not listed. Restore ledgers and results are also encrypted.

Protected checkpoints, the newest snapshot, the newest complete snapshot and the newest nonempty snapshot survive time-based pruning. Ordinary images can expire independently; readers show missing images honestly. Protected images are exempt from the ordinary image budget.

## Recovery

Matching consumes live windows one at a time. A full ordered tab sequence plus active tab must match for ordinary reuse. A partial restore can reuse only a valid ledger target whose recorded signature has not changed.

Missing windows are opened with the Shell `opennewwindow` verb, and distinct new handles are verified. The Windows-specific new-tab command is `WM_COMMAND / 0xA21B`, adapted from ExplorerTabUtility references. It is undocumented, version-sensitive and always followed by count, identity and navigation readback. Tab selection uses UI Automation.

A restore lease prevents recorder snapshots of intermediate restore states. The source remains immutable. New tabs are appended sequentially; each confirmed step is written to the ledger. After completion, placement and stacking are applied, and the resulting tab sequence is checked again.

Existing unrelated windows are never closed by recovery. Taskbar ordering and virtual desktop distribution are references only. Current windows are restored to the current desktop.

## Failure model

Failures remain visible in the result. A missing drive, unregistered new tab, changed target, uncertain order or helper timeout stops that target rather than guessing. API success alone is not verification.

Creation intent is written before opening a window. If that operation is interrupted before its target can be identified, subsequent recovery is blocked instead of opening duplicates. After inspecting existing windows, the user can explicitly opt into `restore --snapshot <id> --explorer --apply --reset-progress`. This discards the ledger and can create another window, so it is never automatic.

Explorer view mode, sorting and exact scroll position are not restored in this preview. Application executables and titles are inventory, not a promise that app internals can be replayed. PrintWindow can yield stale/black images even when its return value indicates success.
