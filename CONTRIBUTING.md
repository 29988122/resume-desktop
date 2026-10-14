# Contributing

[繁體中文首頁](README.md) · [English README](README.en.md)

## Build and verify

Use Windows 11 x64 and the SDK pinned by `global.json`.

```powershell
dotnet build src/ResumeDesktop.App -c Release
dotnet run --project tests/ResumeDesktop.Tests -c Release
dotnet run --project src/ResumeDesktop.App -- probe
```

The core checks are a dependency-free executable test harness. A failing check returns a nonzero exit code. CI runs the same checks.

## Interactive checks

Run only on a desktop where temporary Explorer windows may be opened:

```powershell
dotnet run --project src/ResumeDesktop.App -- smoke --store local-evidence/smoke
```

The smoke scenario creates two test-owned windows with four total tabs, including duplicate paths, checks repeated restoration, closes those test windows and restores a native snapshot. Existing Explorer windows must remain unchanged. It does not restart or terminate Explorer.

Do not introduce `Stop-Process explorer`, process injection, foreground-lock bypasses, clipboard replacement, automatic unpinning or taskbar registry rewrites.

## Changes

Keep Windows interop in the Windows project and matching/storage logic in Core. Add tests for concrete recovery failure modes. Update both READMEs when user behavior changes and update `docs/STATUS.md` when evidence changes.

For UI screenshots use `--demo`, which contains synthetic paths and does not record the real desktop. Do not commit snapshots, private paths, credentials, local logs or screen captures from a real work session.

Include reproduction steps, expected/observed behavior, Windows build, Explorer version and whether the test used real Explorer. Review diagnostics before posting. Maintainers may be volunteers; contributions do not imply a support SLA.
