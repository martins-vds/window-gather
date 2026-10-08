# WinUI 3 migration implementation plan

## Goal and compatibility contract

Replace the WinForms presentation with WinUI 3 and MVVM while keeping the domain
standalone and preserving version 1.2 behavior. Execute and verify the stages
below in order; retain WinForms as a regression reference until parity is proven.
This document is a plan, not a claim that migration or validation is complete.

Gather borrows windows from other monitors, never destination-resident windows.
Restore acts only on borrowed windows whose native identity still matches.
Preserve clickable monitor selection synchronized with the dropdown, Identify
overlays, durable recovery, global configurable shortcuts, tray operations, and
close-to-hide. Show exactly one operation summary and details only for problems.
Keep shortcut reference, registration warnings, and Change shortcuts in a pinned
footer; settings controls must remain fully visible.

### Compatibility hazards to inventory before extraction

- Recovery remains `%LOCALAPPDATA%\WindowGather\session.json`, schema 1.
  Preserve every serialized field through explicit storage DTO mappings and
  retain temporary-write, flush, and atomic-replace behavior.
- Shortcuts remain `%LOCALAPPDATA%\WindowGather\shortcuts.json`. Existing
  WinForms Keys values are numeric: replacement types must translate them
  without changing persisted values. Reserve new registrations, save settings,
  switch mappings, then release old registrations. Precommit failures preserve
  the old registrations.
- Keep mutex `Local\WindowGather.DesktopUtility` across both frontends.
- Save recovery before moving windows. Identity uses handle, process ID,
  process-start ticks, class, and native session markers, not window titles.
  Reject gathering while recovery exists.
- Validate original monitor and layout during Restore. Failed entries remain
  retryable. Persist completion or remaining recovery before removing markers.
  A disk failure must not look like successful completion or absent recovery.
- Preserve classic WINDOWPLACEMENT fields, workspace/physical coordinate
  conversion, negative coordinates, taskbar offsets, normalized minimized and
  maximized states, no-activate/no-z-order movement, mixed-DPI scopes, and
  delayed placement verification. Initially reuse proven native movement.
- Identify remains topmost, tool-window, no-activate, and auto-dismisses after
  three seconds.
- Keep existing defaults, including Ctrl+Alt+F12. Document Microsoft's F12
  reservation and possible registration failure; changing the default is a
  separate product decision.
- Inventory assembly names, public types, friend assemblies, reflection,
  resources, manifests, and runtime dependencies before moving declarations.

## Dependency boundaries

| Project | Dependencies and responsibility |
|---|---|
| Domain (`net10.0`) | BCL only; pure values and rules |
| Application (`net10.0`) | Domain; use cases, ports, coordination |
| Infrastructure.Windows | Application and Domain; native desktop, storage DTOs, Windows adapters |
| Presentation | Application, Domain, CommunityToolkit.Mvvm; framework-independent viewmodels |
| WinUI executable | Presentation and composition dependencies; XAML and native shell lifetime |

Domain must have no Windows target framework, WinUI, WinForms, Toolkit, native
API, persistence, or infrastructure dependency. GatherEngine belongs in
Application, not Domain. Concrete infrastructure construction belongs in the
executable's composition root, not viewmodels. Avoid unnecessary mediator or
DDD machinery.

## Stage 1: inspect and characterize

Read repository instructions and all desktop, storage, shortcut, UI, and test
implementations. Establish a green existing Release build and console harness
baseline, including the existing 32 checks and `--native --ui` modes. Native
tests may manipulate only owned disposable test windows.

Retain or add characterization for identity, resident exclusion, save-before-
move ordering, completion-before-unmark ordering, partial restore, and shortcut
rollback. Record actual baseline results and serialization/public-contract
hazards. Do not start structural extraction without the baseline.

## Stage 2: behavior-preserving extraction

Introduce Domain, Application, and Infrastructure.Windows and keep WinForms as
a consumer. Move pure geometry and values inward, ports and orchestration to
Application, and Windows implementation outward. Introduce explicit legacy
storage DTO mapping where models cross persistence boundaries.

Verify portable behavior, native behavior, persisted legacy recovery and
shortcut fixtures, assembly contracts, and dependency direction before adding
the new presentation. Compilation alone is not compatibility evidence.

## Stage 3: shared operation coordination

Use one application-level coordinator for buttons, tray, hotkeys, Gather,
Restore, and Forget. Reject conflicting requests as busy rather than queuing
them silently; revalidate state inside the gate. Keep blocking native and
storage calls off the UI thread and publish immutable state snapshots.

Pointer-target hotkeys resolve their destination once before moving windows.
Do not introduce unsafe cancellation or alter recovery semantics. Verify
concurrent entry points, busy rejection, disk failures, and state publication.

## Stage 4: Presentation and WinUI

Use .NET 10, C# 14 explicitly, and stable packages; do not use `latest` or
preview language selection. Research identified Windows App SDK 2.5.1 and
CommunityToolkit.Mvvm 8.4.2; verify availability and compatibility before
pinning. Use partial observable properties for WinRT-facing generated types.

Bind monitor preview and dropdown to the same selection. Lock selection during
recovery. Provide no-activate Identify visuals, one summary, error-only details,
persistent shortcut warnings, pinned shortcuts/Change shortcuts, settings
validation/defaults/transaction errors, UI-thread notifications, and genuine
CanExecute invalidation.

Use explicit changing-state binding modes. WinUI Window has no DataContext:
use a typed ViewModel binding or a root/page DataContext. Handle dispatcher
shutdown rejection explicitly. Per-command AsyncRelayCommand protection does
not replace the shared operation coordinator.

## Stage 5: native shell integration

Implement system-wide RegisterHotKey with a lifetime-owned native message
receiver; KeyboardAccelerator is not a global substitute. Use Shell_NotifyIcon
or another non-WinForms adapter for tray menu, reopen, explicit Exit, and
Explorer/taskbar restart recovery. Close hides the window; global shortcuts
continue working while hidden.

Preserve mutex/storage/native movement/DPI/identity behavior. Verify resource
cleanup, restart, hidden-window shortcuts, and tray lifecycle with real UI.

## Stage 6: discoverable tests and quality tooling

Port pure checks to a discoverable test framework. Keep portable, native, and
UI suites distinct. Pin SDK, packages, and local tools. Begin with coherent
VSTest and coverlet.collector integration; do not mix MTP arguments.

Configure separate Stryker Domain and Application runs with fake desktop
services only. Stryker's project setting names a project filename, not a path,
and each run mutates one source project. Research identified Stryker 5.0.0,
ReportGenerator 5.5.11, and coverlet.collector 10.1.0; verify compatibility
before installation.

Generate Cobertura and ReportGenerator method metrics. Implement and test a
transparent method-level CRAP gate that rejects empty/malformed metrics.
CRAP = CC^2 * (1 - coverageFraction)^3 + CC. ReportGenerator's Cobertura method
basis uses line-rate and complexity, with integer rounding; verify the pinned
release using fixtures. OpenCover does not automatically supply CRAP.

Measure line coverage, branch coverage, CRAP, and mutation independently.
Establish measured baselines before ratcheting thresholds. A CRAP warning
above 30 and mutation break/high/low of 80/90/80 are candidate initial policies,
not achieved results. Do not globally suppress equality/logical mutants or
upload source-containing reports to a mutation dashboard.

Add project/assembly dependency checks, CI, and directly related documentation.

## Stage 7: publishing and parity

First establish unpackaged, self-contained folder publishing. Then validate
the documented single-distributable-EXE configuration with extraction:
WindowsPackageType=None, WindowsAppSDKSelfContained=true, SelfContained=true,
EnableMsixTooling=true, IncludeAllContentForSelfExtract=true, and
PublishSingleFile=true.

Retain manifests/resources and inspect actual package-target validation.
Verify distribution startup, recovery, global shortcuts, tray, and restart.
Perform runtime-independent clean-machine validation if available and state
explicitly if it is unavailable. Preserve the old release until parity is
established. Update build, usage, and architecture documentation and provide
verified source/artifact locations.

## Completion evidence

Completion requires persistent implementation, explicit relevant builds and
tests, real UI/distribution checks, and meaningful mutation and CRAP outputs.
Record genuine verification limitations rather than claiming parity from a
scaffold or fake-only successful build.

## Primary references

- [C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- [Language version](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)
- [WinUI getting started](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=command-line)
- [MVVM WinRT observable-property guidance](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0045)
- [Relay commands](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/generators/relaycommand)
- [DispatcherQueue](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue)
- [Unpackaged single-file publishing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app#single-file-exe)
- [Windows App SDK project properties](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/project-properties)
- [Stryker configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
