# GammaSwitch Implementation Plan

**Goal:** Deliver a Chinese Windows 10 SDR tray application that switches the user's existing ICC/VCGT files with freely configurable hotkeys on independent monitors.

**Architecture:** A dependency-free core owns bounded ICC parsing, action validation, atomic JSON storage and transactional display operations. A WinForms host owns Windows display/color APIs, native global hotkeys, the editor and tray lifecycle. Native diagnostics are read-only; real gamma writes are only triggered by explicit user actions.

**Tech stack:** C# / .NET 10, WinForms, Win32, console-based executable behavioral tests (no external test packages).

**Spec:** Approved conversation: existing P27UEA.icm / tarkov.icc / tarkov_brighter.icc; assign any supported shortcut; per-display profiles and multi-display scenes; restore original settings.

## Constraints

- No automatic display changes or preassigned shortcuts on first launch.
- Stable monitor identity; reject disconnected, ambiguous, mirrored targets.
- Preserve three independent 16-bit gamma channels; support table VCGT and reject malformed/unsupported tags clearly.
- ICC without VCGT uses saved baseline in combined mode, not an invented linear ramp.
- Save recovery data before mutation; roll back partial multi-display failures.
- Hotkey edits validate duplicates and OS conflicts; a failed edit retains previous bindings.
- Normal close minimizes to tray; explicit Exit offers baseline restoration; pending recovery persists across abnormal exit.

## Task 1: File and configuration behavior

- [x] Add executable tests using hand-built binary ICC fixtures and real temporary files. Tests cover 8/16-bit table interpolation, RGB separation, absent VCGT, malformed offsets, formula rejection, duplicate and bare-key hotkeys, JSON persistence.
- [x] Run `dotnet run --project tests/GammaSwitch.Tests` and confirm the behavioral assertions fail against explicit unimplemented methods.
- [x] Implement `Models.cs`, `IccProfile.cs`, `Configuration.cs`, `JsonStore.cs` until these tests pass.

## Task 2: Recoverable multi-monitor switching

- [x] Add tests using an in-memory implementation of the native display boundary: baseline saved before mutation; a missing target causes no changes; partial failure restores both touched screens; ICC without VCGT restores saved gamma; failed recovery remains on disk.
- [x] Implement `SwitchService.cs` and verify with the same test command.
- [x] Implement `WindowsDisplayBackend.cs` and `NativeMethods.cs`; inspect real displays and default ICCs through `--diagnose <path>` without writes.

## Task 3: Freely configurable hotkeys and Chinese UI

- [x] Implement atomic native hotkey re-registration; editor capture with modifiers; no-repeat registration and suspend while editing.
- [x] Implement `MainForm.cs`, `ActionEditor.cs`, `HotkeyBox.cs`, `Program.cs`: target/profile grids, individual and scene actions, cycle/restore actions, tray menu, conflict errors, persistent settings and recovery prompt.
- [x] Build: `dotnet build src/GammaSwitch -c Release`.
- [x] Launch and inspect real UI; create/edit/capture/save/reopen a shortcut and clear it without applying gamma.

## Task 4: Delivery

- [x] Re-run all behavioral tests; run read-only diagnostics against all three actual ICCs and attached monitors.
- [x] Publish a Windows x64 self-contained single-file executable, provide build/test scripts and Chinese README with runtime limitations and recovery instructions.
- [x] Verify published binary starts; report native writes/game compatibility as unverified unless explicitly tested.
