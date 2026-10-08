# Window Gather

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
The application is unsigned. Source code is included so you can inspect and build it.

## Build

Requires Windows and the .NET 10 SDK. There are no third-party NuGet dependencies.

```powershell
dotnet build .\WindowGather\WindowGather.csproj -c Release
dotnet run --project .\WindowGather.Tests\WindowGather.Tests.csproj -c Release
dotnet run --project .\WindowGather.Tests\WindowGather.Tests.csproj -c Release -- --native --ui
dotnet publish .\WindowGather\WindowGather.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .\release
```

The native tests move only their own disposable test windows, never your
application windows. Multi-monitor movement tests require two or more displays.
The portable publish includes .NET, so the resulting executable needs no separate runtime.

The default shortcuts are **Ctrl+Alt+F11** to gather onto the monitor under your
pointer and **Ctrl+Alt+F12** to restore. Use **Change shortcuts...** to save your
own combinations.
