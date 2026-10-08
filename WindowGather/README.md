# Window Gather

A free, local Windows utility that temporarily brings application windows onto
one display, then restores only those borrowed windows. Windows already on the
destination are excluded from both operations.

## Run

Open `WindowGather.WinUI.exe` from the new publish output, or `WindowGather.exe`
from the retained 1.2 WinForms release. Self-contained distributions include .NET
and the new frontend also includes Windows App SDK: no installation,
subscription, AutoHotkey, or separate runtime is required. It is an unsigned
application, not a Microsoft- or commercially-signed product. Review the included
source if your security policy requires it.

When upgrading, exit the old version through its notification-area menu before
opening the new executable. Existing recovery data is kept and remains compatible.

1. Click **Identify displays** to show a large matching label on each monitor
   for three seconds, without moving your windows or taking keyboard focus.
2. Click your treadmill display in the preview to select it. The dropdown is
   kept as a keyboard-accessible alternative; both selections stay in sync.
3. Click **Gather to selected display**.
4. When finished, click **Restore borrowed windows**.

All displays can remain connected and enabled. The destination does not need to
be the primary display. The preview matches the extended desktop arrangement;
the picker also shows the monitor's device name, model, and resolution.
Identification labels use the same device names as the app's preview and
dropdown; these are not necessarily the numbers shown in Windows Settings.
Selecting a display does not move any windows until you click Gather. Selection
is locked during a gather session, but Identify remains available.

### Shortcuts and tray

- **Ctrl+Alt+F11** (default): gather onto the display under the mouse pointer. The pointer
  position is read once, before any windows are moved.
- **Ctrl+Alt+F12** (default): restore the current session.
  Windows reserves F12 for debuggers, so this legacy default may fail to register.
  It is intentionally preserved; choose another key if a warning appears.
- Click **Change shortcuts...** (also available in the tray menu) to choose
  Ctrl, Alt, Shift, and/or Win plus a letter, number, or function key for each
  action. At least Ctrl, Alt, or Win is required, and the actions must differ.
  **Save shortcuts** applies the new combinations immediately and saves them
  at `%LOCALAPPDATA%\WindowGather\shortcuts.json`. **Use defaults** fills in
  the original combinations; click Save to apply them.
- Current shortcuts are always shown below the operation status. Registration
  conflicts stay visible independently of operation results. If a replacement
  is unavailable or saving fails, the previous working shortcuts are kept.
- Closing the window keeps the utility in the notification area. Double-click
  its icon to reopen it; right-click for display actions, restore, or exit.
  The WinUI tray adapter recreates its icon after Explorer/taskbar restart.
- Hotkey conflicts appear beside the shortcut reference. Buttons and tray commands remain
  available.

Successful operations show their summary once. The details box appears only
when there are errors or unresolved windows; it is hidden after a successful
operation.

## Recovery and behavior

The return record is saved **before moving windows**, at
`%LOCALAPPDATA%\WindowGather\session.json`. Exiting and restarting the utility
preserves recovery while the same application windows remain alive. A second
gather is blocked until you restore or explicitly forget the existing session.

Normal, maximized, and minimized window states are preserved. Minimized windows
remain minimized; their restore destination moves to the chosen monitor. Large
normal windows are fitted into the target's work area where the application
permits. Restoration uses the original window placement.

Only visible, non-cloaked top-level application windows on the current virtual
desktop are considered. The shell, taskbars, tool/no-activate windows, and Window
Gather itself are excluded. Visible owned dialogs may be included. New windows
opened after gathering are not restored. Closed/replaced windows are skipped:
per-window session markers plus process identity checks prevent matching a
different window merely because its title or handle resembles the original.
No window titles, browser URLs, or document contents are stored.

Missing origin displays and changed display arrangements retain recovery
entries. Reconnect the original display or restore its original position,
resolution, and taskbar arrangement, then retry Restore. Physical display
identity is used rather than relying only on enumeration numbers.

**Forget recovery** requires confirmation. It leaves windows where they are and
removes the return record. **Exit** does not forget recovery.

## Limitations

- Recovery cannot resurrect closed applications or survive a Windows reboot
  with the original windows intact. It survives a utility restart, not an
  application restart.
- Elevated/protected apps may refuse capture, session markers, or movement.
  Failures appear in the details area. If needed, start Window Gather as
  administrator yourself; it does not elevate automatically.
- Native Windows Snap groups/zone metadata and window stacking order are not
  restored. Normal placement and minimized/maximized state are restored.
- Some full-screen, custom, fixed-size, or modal applications control their own
  placement. A failed move is reported, and its recovery record is retained.
- Changing DPI scaling or monitor topology mid-session may prevent exact
  placement recovery. Keep the display setup stable for the gather/restore cycle.
- A minimized window's "maximize when next restored" behavior depends on the
  placement flags Windows exposes; ordinary minimized/maximized states are
  preserved, but unusual application-specific restore behavior is not guaranteed.
- There is no startup registration, networking, telemetry, auto-update, display
  disabling, or alteration of your Windows display settings.

## Build and validate

Requires Windows and the SDK pinned in `global.json`. The new frontend uses
WinUI 3 and CommunityToolkit.Mvvm; the standalone Domain and Application projects
have no third-party packages. Run these commands from the repository root:

```powershell
.\scripts\Verify.ps1
.\scripts\Verify.ps1 -Native -LegacyUi -Mutation
.\scripts\Publish.ps1 -Format Folder
.\scripts\Publish.ps1 -Format SingleFile
```

The native tests create and move **only their own disposable test windows**.
They never gather your application windows. Multi-monitor cases require at least
two connected displays; unavailable cases are explicitly skipped.

Build the isolated WinUI XAML parity harness with:

```powershell
dotnet publish .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release `
  -p:UiTestBuild=true -o .\artifacts\ui-test
```

Run its executable on an interactive desktop. It uses fake desktop services and
a disposable recovery directory, writes `ui-parity-results.txt`, and exits.
It does not touch your recovery file or move your application windows. This test
composition is compile-time-only and is absent from ordinary builds/publishing.
Native movement and native shell tests remain separate; passing the fake UI
harness alone is not evidence of native placement correctness.
