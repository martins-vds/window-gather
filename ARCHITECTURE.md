# Architecture and verification

## Dependency direction

| Project | Responsibility | Dependencies |
| --- | --- | --- |
| `WindowGather.Domain` | Pure geometry, window/display values and shortcut rules | BCL only; portable `net10.0` |
| `WindowGather.Application` | Gather/Restore/Forget orchestration, ports, shortcut transactions and operation coordinator | Domain; portable `net10.0` |
| `WindowGather.Infrastructure.Windows` | Native desktop operations, recovery/shortcut DTOs and storage, global hotkeys, tray and Identify overlays | Application/Domain; Windows |
| `WindowGather.Presentation` | Observable state, commands, shortcut editor and UI-dispatcher port | Application/Domain, CommunityToolkit.Mvvm; portable `net10.0` |
| `WindowGather.WinUI` | XAML, dialogs, window lifecycle and composition root | Presentation and Windows infrastructure |

`GatherEngine` is application orchestration, not a domain aggregate. Domain has
no Windows target, persistence annotations, native API, WinForms, WinUI or
Toolkit dependency. Viewmodels never construct Windows adapters.
`App.xaml.cs` is the composition root. Project and compiled-assembly tests
enforce these boundaries, including rejection of native imports in portable
assemblies.

The original WinForms frontend remains a regression consumer, and the existing
1.2 release is not replaced or deleted. Original public model namespaces and
legacy executable type forwarders preserve the extracted types' assembly
resolution. Characterization tests cover forwarders and persisted contracts;
this is not a claim that every possible external reflection consumer was tested.

## Preserved contracts and concurrency

- `%LOCALAPPDATA%\WindowGather\session.json` keeps schema 1 and the original
  field names through explicit infrastructure DTO mappings. Writes retain
  temporary-file creation, flush-to-disk and atomic replacement.
- `%LOCALAPPDATA%\WindowGather\shortcuts.json` retains numeric Windows
  virtual-key values. The portable `ShortcutKey` replaces WinForms `Keys`
  without rewriting existing settings. Shortcut replacement reserves new
  registrations, saves, switches mappings, then releases old registrations;
  failures before commit retain the previous registrations.
- `Local\WindowGather.DesktopUtility` excludes concurrent legacy/WinUI
  frontends. Close hides the window; explicit Exit releases native resources.
- Gather saves recovery before moving anything, excludes destination-resident
  windows and blocks a second Gather while recovery exists. Identity uses
  handle, process ID/start time, class and per-session native markers, not titles.
- Restore checks original physical monitors/layout and retains failed entries
  for retry. Resolved completion or remaining recovery is persisted before
  unmarking. Storage failures are reported rather than treated as success.
- The proven native `WINDOWPLACEMENT`, workspace/physical-coordinate, DPI,
  minimized/maximized, no-activation and delayed-verification behavior remains
  in `NativeDesktop`. WinUI `AppWindow` is not used to move other applications.

One `OperationCoordinator` gates button, tray and hotkey use cases, including
Restore and Forget. Conflicting requests get an explicit busy response rather
than being queued. State is revalidated inside the gate; pointer-target Gather
resolves the display once before moving windows. Blocking native/storage work
runs off the UI thread, without unsafe cancellation of an operation in progress.
Immutable snapshots carry increasing revisions so viewmodel/tray dispatchers
can reject stale notifications. Rejected shutdown notifications are logged.

The preview and dropdown share one selected display. Recovery locks selection.
The main content scrolls independently of the pinned shortcut/warning footer.
There is one completion summary, with details only for actual problems.
Generated commands receive real CanExecute invalidation.

`WindowsShell` owns a lifetime-bound native message receiver, `RegisterHotKey`
registrations and `Shell_NotifyIcon` tray integration. Registration is marshaled
to the receiver's owning thread. The tray supports reopening, operations and
explicit exit; `TaskbarCreated` restores its icon after Explorer restarts.
Identify uses topmost, tool, no-activate native labels and dismisses after three
seconds. Local verification simulated taskbar recreation; it did not kill
Explorer. The legacy Ctrl+Alt+F12 default is preserved, with its documented
Windows debugger reservation shown in settings.

## Toolchain and quality gates

`global.json` pins SDK 10.0.401, and `Directory.Build.props` selects stable
C# 14 explicitly. Windows App SDK is 2.5.1; CommunityToolkit.Mvvm is 8.4.2.
NuGet lock files and the local tool manifest pin dependencies and tools.
Testing uses VSTest, xUnit and coverlet.collector, not the separate MTP
integration. Local tools are Stryker 5.0.0 and ReportGenerator 5.5.11.

```powershell
.\scripts\Verify.ps1
.\scripts\Verify.ps1 -Native -LegacyUi -Mutation
```

The portable suite uses fake desktop services. Native tests touch only owned,
disposable windows and need an interactive multi-monitor Windows desktop.
The retained WinForms UI suite is distinct from the WinUI XAML parity harness.
On an ARM64 SDK host the portable test executable targets ARM64 so Stryker's
Windows VSTest discovery does not accidentally require an absent x64 .NET
installation; production portable assemblies remain AnyCPU.

Measured local results after the migration:

| Measure | Domain | Application |
| --- | ---: | ---: |
| Line coverage | 100% | 100% |
| Branch coverage | 96.96% | 93.05% |
| Mutation score | 100% | 95.54% |
| Killed / survived eligible mutants | 77 / 0 | 107 / 5 |

The portable suite passed 78 tests. The combined retained/native/UI harness
passed 35 checks. Across 54 reported core methods, the maximum method CRAP
was 20 (`GatherEngine.Restore`). The initial measured mutation baseline was
85.71% Domain and 72.32% Application; additional behavior assertions improved
it before enabling an 80% break threshold (high 90%, low 80%).
Compilation-error and default Stryker block-filter exclusions are not counted
as kills. Five Application mutants still survive; there are no blanket custom
logical/equality exclusions or mutation-dashboard uploads.

Line coverage, branch coverage, mutation and CRAP are separate measures.
The tested method gate reads ReportGenerator's per-class XML generated from
Cobertura. It validates method line coverage, cyclomatic complexity and the
rounded formula `CC^2 * (1 - lineCoverageFraction)^3 + CC`. Empty, malformed,
nonfinite or missing core-assembly metrics fail the gate. This tooling uses
method line coverage, not the original CRAP definition's basis-path coverage.
Current gates require each core assembly's line coverage >=95%, branch
coverage >=80% and every reported core method's CRAP <=30. These are
baseline policies, not a substitute for behavioral/native testing.

Reports stay under ignored `artifacts`: fresh coverage runs have unique
`coverage-<id>\report` directories; mutation reports are in
`mutation-domain\reports` and `mutation-application\reports`.
The GitHub workflow builds, runs portable quality gates, publishes both x64
formats and runs separate mutation jobs. The workflow itself has not yet been
executed on GitHub; interactive native/UI checks are deliberately local.

## WinUI parity and distribution

An isolated compile-time test composition uses fake desktop services and
disposable storage, never the user's recovery file. To reproduce:

```powershell
dotnet publish .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release -r win-x64 --self-contained true -p:UiTestBuild=true "-p:NuGetLockFilePath=obj\packages.ui-test.lock.json" -o .\artifacts\ui-test
# Launch artifacts\ui-test\WindowGather.WinUI.exe on an interactive desktop.
# Inspect artifacts\ui-test\ui-parity-results.txt after it exits.
```

The real-XAML harness passed synchronized selection, command invalidation,
recovery selection lock, durable recovery, resident exclusion, partial Restore,
error visibility, pinned footer, one summary, restart, close-to-hide and reopen.
Ordinary builds explicitly select `UiTestBuild=false` and do not include this
test composition. Fake desktop parity does not replace native movement tests.

```powershell
.\scripts\Publish.ps1 -Format Folder
.\scripts\Publish.ps1 -Format SingleFile
.\scripts\Publish.ps1 -Format Folder -Runtime win-arm64
```

The x64 folder and single-file distributions were built and launched locally.
Both loaded existing recovery, kept selection locked and displayed the pinned
footer; the folder distribution's shortcut dialog was inspected with all
controls visible. Neither smoke test moved windows or changed recovery or
saved shortcuts. The single-file directory contains exactly
`WindowGather.WinUI.exe` (232,677,970 bytes at this verification).

Single-file means one **distributable** executable with runtime extraction,
not extraction-free execution. Publishing sets `WindowsPackageType=None`,
`WindowsAppSDKSelfContained=true`, `SelfContained=true`,
`EnableMsixTooling=true`, `PublishSingleFile=true` and
`IncludeAllContentForSelfExtract=true`. It uses separate ignored
`obj\packages.publish-<RID>-<Format>.lock.json` restore graphs so RID/ILLink
dependencies do not alter checked-in ordinary restore locks.

Outputs are `artifacts\publish-win-x64-folder`,
`artifacts\publish-win-x64-singlefile` and
`artifacts\publish-win-arm64-folder`. Native ARM64 folder publishing passed,
but its executable has not been run. x64 startup passed under emulation on
the ARM64 host without an installed x64 .NET host. No clean VM or entirely
runtime-free Windows installation was available, so clean-machine validation,
older supported Windows versions and an actual Explorer restart remain
unverified. These local artifacts are not a new signed release.
