# Window Gather usage

A free, local Windows utility that temporarily brings application windows onto
one display, then restores only those borrowed windows. Windows already on the
destination are excluded from both operations.

## Run

Open `WindowGather.WinUI.exe` from a current portable package, or `WindowGather.exe`
from the retained 1.2 WinForms release. Self-contained distributions include .NET
and the new frontend also includes Windows App SDK: no installation,
subscription, AutoHotkey, or separate runtime is required. Releases are unsigned
by default; `release-manifest.json` declares whether Azure Artifact Signing was
required and verified. Review the included source and follow your security policy.

When upgrading, exit the old version through its notification-area menu before
opening the new executable. Existing recovery data is kept and remains compatible.
The EXE is a single distributable file that extracts its bundled runtime
contents on startup. Keep the accompanying `source.zip` (or the `source` folder
in older releases) to inspect or rebuild the exact release; it is not required
to run the utility. Run the EXE directly, not with `dotnet`.

1. Click **Identify displays** to show a large matching label on each monitor
   for three seconds, without moving your windows or taking keyboard focus.
2. Click your treadmill display in the preview to select it. The dropdown is
   kept as a keyboard-accessible alternative; both selections stay in sync.
3. Click **Gather to selected display**.
4. When finished, click **Restore session** (**Restore borrowed windows** in older releases).

All displays can remain connected and enabled. The destination does not need to
be the primary display. The preview matches the extended desktop arrangement;
the picker also shows the monitor's device name, model, and resolution.
Identification labels use the same device names as the app's preview and
dropdown; these are not necessarily the numbers shown in Windows Settings.
Selecting a display does not move any windows until you click Gather. Selection
is locked during a gather session, but Identify remains available.

### Optional primary-display behavior (current source)

These options are **not yet in the published v1.0.0 release**. Both default off
and are saved separately in `%LOCALAPPDATA%\WindowGather\gather-options.json`.
The preview/picker label the current primary display.

- **Make the gathering destination the primary display** changes Windows' primary
  monitor when gathering. Explicit Restore reverses that change before restoring
  borrowed windows, provided the original monitor is available and you have not
  overridden the primary yourself. A failure stops Gather before app-initiated
  window movement; any primary recovery remains saved.
- **New windows follow the primary display on Restore** is available with the
  first option. It moves only confirmed eligible windows opened after Gather on
  the destination that are still there when you explicitly Restore. They follow
  the effective primary; borrowed windows still return to their saved origins.
  Pre-existing destination residents and windows you moved elsewhere are excluded.

Options are fixed for a session and locked until recovery is completed or
forgotten. Buttons, tray and global shortcuts use the same saved options.
New-window following is not continuous. If you manually change the primary,
Restore respects that choice instead of switching it back; recorded windows follow
that primary. This includes an away-and-back change observed while the app runs.
Unsupported/cloned or ambiguous display arrangements are refused, not guessed.

Primary changes can help apps that use the default monitor, but notification
routing and application launch locations remain controlled by Windows/the apps.
Some apps remember their own monitor. Shell notifications/tool windows are not
eligible followers. Windows may reposition unrelated windows when primary changes;
Window Gather does not explicitly enroll or move existing residents.

Closing to the tray keeps tracking. Exit/restart interrupts observation: only
durably recorded new windows can follow; no new membership is guessed on restart.
If a switch/reversal or ownership history was interrupted, Restore can require you
to select the intended primary in Windows display settings and retry rather than
overwrite a possible manual change. Read the reported recovery details.
If the original primary is unavailable, reconnect it and explicitly Restore;
safe borrowed returns can still complete while primary/follower recovery stays pending.

Disconnect automation continues returning **borrowed windows only**; it never
changes primary settings or follows new windows. Reconnect does not trigger moves.
**Forget recovery** abandons both window and primary recovery without changing
Windows settings; the temporary primary can remain primary.
An empty borrowed list can still have pending primary/follower work.
Use the current WinUI frontend for these sessions. The updated legacy frontend
refuses unsupported primary Restore; older released binaries cannot understand the
new metadata and must not be used to Restore/Forget such a session.

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

The native interface follows your Windows app theme, including a matching title
bar and system accent color. The monitor preview highlights the selected display
and shows resolutions where space permits. Gather/Restore appear side by side
in wider windows and stack in compact windows; the shortcut footer stays pinned.

## Recovery and behavior

The return record is saved **before moving windows**, at
`%LOCALAPPDATA%\WindowGather\session.json`. Exiting and restarting the utility
preserves recovery while the same application windows remain alive. A second
gather is blocked until you restore or explicitly forget the existing session.

Normal, maximized, and minimized window states are preserved. Minimized windows
remain minimized; their restore destination moves to the chosen monitor. Large
normal windows are fitted into the target's work area where the application
permits. Restoration uses the original placement when the physical origin's
layout is unchanged. If its position, resolution or work area changed, Restore
fits the saved normal rectangle relative to the current work area while keeping
the saved minimized/maximized state. It does not replace the original return record.

Only visible, non-cloaked top-level application windows on the current virtual
desktop are considered. The shell, taskbars, tool/no-activate windows, and Window
Gather itself are excluded. Visible owned dialogs may be included. By default,
new windows opened after gathering are not restored; the opt-in follow rule above
is the only exception. Closed/replaced windows are skipped:
per-window session markers plus process identity checks prevent matching a
different window merely because its title or handle resembles the original.
No window titles, browser URLs, or document contents are stored.

In the current WinUI source, disconnecting the gathering destination
automatically returns borrowed windows to available original physical monitors.
If only an origin disconnects, the app leaves gathered windows on the destination.
Windows itself may reposition windows during either change; Window Gather cannot
prevent that.

Unavailable origins and failed returns remain saved, with a persistent warning.
No substitute origin is chosen. Reconnecting monitors refreshes availability but
does **not** automatically move pending windows: click **Restore session**
to retry. Automatic return waits for the layout to settle and never interrupts
an operation already moving windows. It neither brings the app to the foreground
nor moves destination-resident/new windows.

Recovery loaded at startup with its destination already absent also requires
explicit Restore. An unavailable or unreadable display inventory is not treated
as a disconnect. Physical display identity is used instead of enumeration numbers.
These changes ship in the published v1.0.0 WinUI release. Historical local 2.0.1
artifacts and legacy release binaries have not been replaced. Tags are the
release version source; a larger historical local version is not a newer release.

**Forget recovery** requires confirmation. It leaves windows where they are and
removes the return record. **Exit** does not forget recovery.

## Start with Windows (current source)

**Start with Windows** is off by default. Enable it in the main window to launch
quietly in the notification area after you sign in. Hotkeys and tray commands
remain available; use **Open** in the tray to show the window. A normal manual
launch still opens the main window. Startup does not Gather or explicitly Restore
anything, and a second startup launch exits quietly if the app is already running.
The existing destination-disconnect policy is unchanged.

The option registers only the current user, without elevation, using the
`WindowGather` value in
`HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
It stores the quoted **outer executable** path with `--startup`, not an extracted
single-file runtime path. There is no extra JSON preference that can drift from
this registration. Disabling removes only this app's recognized entry, not other
startup applications. Read/write/verification failures are visible and never
reported as a successful save.

Keep the executable in a permanent location before enabling this option.
After moving or replacing its folder, run the new copy and enable the option
again to update the path. An old recognized path produces a warning; unknown
commands are not overwritten or deleted. A command longer than Windows'
260-character Run limit must use a shorter path.

Windows **Settings > Apps > Startup**, organizational policy, or security
software can block/delay launch independently. This switch manages the Run entry;
it does not override Windows' startup approval settings. If already disabled
there, re-enable Window Gather in Windows as well. Deleting the portable folder
does not unregister it: turn this option off first. This feature is not included
in the existing immutable v1.0.0 release.

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
- Changing DPI scaling or monitor topology can prevent exact original pixel
  placement. Changed work areas are fitted safely; original logical-size/DPI and
  application-specific placement restoration are not guaranteed.
- A minimized window's "maximize when next restored" behavior depends on the
  placement flags Windows exposes; ordinary minimized/maximized states are
  preserved, but unusual application-specific restore behavior is not guaranteed.
- There is no networking, telemetry, auto-update or display disabling.
  Startup registration and primary-display changes are explicit, off-by-default
  options in current source.

## Build and validate

Requires Windows and the SDK pinned in `global.json`. The new frontend uses
WinUI 3 and CommunityToolkit.Mvvm; the standalone Domain and Application projects
have no third-party packages. Run these commands from the repository root:
In current portable packages, extract the accompanying `source.zip` first.
Older packages may contain an expanded `source` folder.

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
  -p:UiTestBuild=true -p:UiPreviewBuild=false `
  "-p:NuGetLockFilePath=obj\packages.ui-test.lock.json" -o .\artifacts\ui-test
```

Run its executable on an interactive desktop. It uses fake desktop services and
a disposable recovery directory, writes `ui-parity-results.txt`, and exits.
It does not touch your recovery file or move your application windows. This test
composition is compile-time-only and is absent from ordinary builds/publishing.
Native movement and native shell tests remain separate; passing the fake UI
harness alone is not evidence of native placement correctness.

For an interactive design preview, build with both `UiTestBuild=true` and
`UiPreviewBuild=true` into a separate output directory. Its title explicitly
identifies simulated windows; all operations and shortcut changes use disposable
test state. Use the footer's Exit button to end the preview. Neither flag is
enabled in ordinary builds or release publishing.
