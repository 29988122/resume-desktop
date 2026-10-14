# Verification receipt

Host validation date: 2026-10-14. This receipt intentionally excludes personal paths, handles, screenshots of real work and encrypted session payloads.

- Windows 11 Education 25H2, build 26200; Explorer 10.0.26100.8875.
- Release build: zero warnings and zero errors.
- Core checks: 27 checks, including duplicate multiplicity, one-to-one matching, partial retry, unresolved creation, atomic storage and protected history.
- Native smoke: two Explorer windows, four tabs, duplicate folder paths and active-tab readback.
- Repeating recovery did not add windows.
- Test-owned windows were confirmed closed before native snapshot recovery.
- Explorer relative stacking was verified. Anchoring against unrelated application windows remains best effort.
- Existing Explorer window/tab sequences were preserved.
- DPAPI-protected snapshot and ledger roundtrip passed.
- A bounded local visual capture completed; no real-work-session images are included here.
- WPF demo rendering checked at 1080x740 and 780x520 using synthetic data.
- Actual Explorer process termination, Windows reboot, multi-monitor/DPI transitions and extended recorder soak were not performed.

Reproduce with the commands in CONTRIBUTING.md. Live integration evidence must remain local unless manually reviewed and redacted.
