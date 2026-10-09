# Window Gather 2.0.1

A portable Windows utility that temporarily gathers application windows onto any
monitor and restores only the borrowed windows to their original monitors and
placements. Windows already on the destination stay untouched.

## Features

- Click a monitor in the desktop preview to choose the destination.
- Identify physical displays with temporary labels that do not steal focus.
- Preserve normal, minimized, and maximized states.
- Save return positions before moving windows and recover after restarting the utility.
- Customize global Gather and Restore shortcuts, with conflict reporting.
- Use the notification-area menu while the main window is closed.
- Run locally without installation, telemetry, subscriptions, or display-setting changes.

Read the [usage guide, recovery behavior, and limitations](WindowGather/README.md).
Releases are unsigned by default; optional Azure Artifact Signing must be
configured and verified before enabling it. Source code accompanies each package.

The 2.0.0 release replaces the presentation with WinUI 3/MVVM while retaining
the existing recovery and shortcut formats. The legacy frontend stays at 1.2.0.
Version 2.0.1 adds the polished native layout, system-aware styling and responsive
monitor/action presentation without changing recovery or window movement.
Current source additionally handles destination disconnects with one automatic
return attempt and durable pending recovery; reconnects require explicit Restore.
The already-generated 2.0.1 release binaries remain unchanged.

## Build

Requires Windows and the .NET SDK pinned by `global.json` (10.0.401).
The migrated frontend uses **WinUI 3 / Windows App SDK 2.5.1** and
**CommunityToolkit.Mvvm 8.4.2**, with **C# 14** explicitly selected.
Domain and Application target portable `net10.0` and have no third-party packages.

```powershell
.\scripts\Verify.ps1
.\scripts\Verify.ps1 -Native -LegacyUi -Mutation
.\scripts\Publish.ps1 -Format Folder
.\scripts\Publish.ps1 -Format SingleFile
.\scripts\Release.ps1 -Tag v2.1.0 -Runtime win-x64
.\scripts\Release.ps1 -Tag v2.1.0 -Runtime win-arm64
```

The native tests move only their own disposable test windows, never your
application windows. Multi-monitor movement tests require two or more displays.
Native/UI suites require an interactive Windows desktop; CI runs portable tests,
both frontend builds, publishing, and separate core mutation runs.

Launch `artifacts\publish-win-x64-folder\WindowGather.WinUI.exe`, or the
single executable in `artifacts\publish-win-x64-singlefile`. The single-file
configuration extracts its self-contained .NET and Windows App SDK contents
at runtime; it is **one distributable file**, not an extraction-free application.
`-Runtime win-arm64` selects the native ARM64 distribution.

`Release.ps1` requires a clean committed checkout and writes immutable candidate
packages under `artifacts\releases\<tag>\<RID>`, including the portable EXE,
usage documentation, exact committed `source.zip`, `VERSION`, release metadata
and SHA-256 checksums. The release tag controls CLR/native/app-facing versions.
GitHub Actions supports tag/manual releases with scoped optional signing and
no automatic replacement of existing assets. See [release packaging and required
GitHub/signing settings](RELEASE.md). Local packaging never publishes a GitHub
Release or creates a tag. Existing 2.0.0/2.0.1 artifacts are preserved.

The original WinForms project and existing 1.2 release are retained as a
regression reference. Exit either frontend before launching the other: both
use the same mutex, recovery schema, storage directory, and numeric shortcut
settings. See [architecture, quality gates, and verification](ARCHITECTURE.md)
and the [implementation plan](WINUI-MIGRATION-PLAN.md).

The default shortcuts are **Ctrl+Alt+F11** to gather onto the monitor under your
pointer and **Ctrl+Alt+F12** to restore. Use **Change shortcuts...** to save your
own combinations.
Windows reserves F12 for debuggers, so the legacy Restore default may fail to
register. The default is preserved rather than silently rewriting saved settings;
choose another key in Change shortcuts if registration fails.
