# Contributing to Window Gather

Start with an issue describing the problem or change. Small, focused pull
requests are easier to review. Licensing remains an owner decision: this
repository currently grants no project reuse license.

## Prerequisites

- Windows 10 1809+ or Windows 11; an interactive desktop for native/UI checks.
- Git and PowerShell 7 (`pwsh`).
- The .NET SDK selected by `global.json` (currently 10.0.401).
  Follow Microsoft's SDK installation guidance; do not loosen the pin to make
  a failing build pass. No Visual Studio workload is required by the scripts.

```powershell
git clone https://github.com/martins-vds/window-gather.git
Set-Location window-gather
dotnet --version
.\scripts\Verify.ps1
```

`Verify.ps1` restores pinned local tools and locked packages, runs portable
tests, dependency/release contracts, coverage and method CRAP gates, and builds
both frontends. Fresh results go under ignored `artifacts\coverage-<id>`.
For a smaller code iteration:

```powershell
dotnet test .\WindowGather.Core.Tests\WindowGather.Core.Tests.csproj -c Release
dotnet build .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release -p:UiTestBuild=false
```

## Run and publish locally

```powershell
.\scripts\Publish.ps1 -Format Folder
.\artifacts\publish-win-x64-folder\WindowGather.WinUI.exe
.\scripts\Publish.ps1 -Format SingleFile
# Native ARM64:
.\scripts\Publish.ps1 -Format SingleFile -Runtime win-arm64
```

Normal app runs operate on your desktop and real recovery/settings. Exit other
Window Gather versions first: frontends share a mutex and storage location.
For safe UI exploration instead, use the isolated preview described below.

## Test safely

Portable tests use fake desktop services; they do not gather real user windows.
Normal CI also builds/publishes without launching the app. Mutation runs are
expensive and separate from the fast local loop:

```powershell
.\scripts\Verify.ps1 -Mutation
# Only on an interactive Windows desktop:
.\scripts\Verify.ps1 -Native -LegacyUi
```

Native tests create/move only owned disposable windows. Multi-monitor cases
need two connected displays; missing cases are explicitly skipped. Never
validate by gathering someone else's windows or editing their recovery file.
Do not run native/GUI checks on unsuitable hosted runners.

The real-XAML test and preview use simulated displays/windows and disposable
state; they do not register production shortcuts or move your applications:

```powershell
dotnet publish .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release -r win-x64 `
  --self-contained true -p:UiTestBuild=true -p:UiPreviewBuild=false `
  "-p:NuGetLockFilePath=obj\packages.ui-test.lock.json" -o .\artifacts\ui-test
.\artifacts\ui-test\WindowGather.WinUI.exe
Get-Content .\artifacts\ui-test\ui-parity-results.txt
```

For an interactive preview, set `UiPreviewBuild=true` and use a separate output
directory. The title labels simulated windows. Exit through the footer.
These flags must remain **false** in production builds/releases.

## Project map and boundaries

| Project | Responsibility |
|---|---|
| `WindowGather.Domain` | Pure values/rules; BCL only, portable `net10.0` |
| `WindowGather.Application` | Use cases, ports, operation coordinator; depends on Domain |
| `WindowGather.Infrastructure.Windows` | Native desktop/shell, storage and shortcut adapters |
| `WindowGather.Presentation` | Framework-independent MVVM; depends inward |
| `WindowGather.WinUI` | XAML/native shell composition, not domain rules |
| `WindowGather` | Retained WinForms regression frontend |
| `WindowGather.Core.Tests` / `WindowGather.Tests` | Portable / interactive native tests |
| `WindowGather.Quality` | Transparent coverage and method CRAP gate |

Preserve schema-1 recovery fields/numeric shortcuts, physical monitor/window
identity, resident exclusion, save-before-move and persist-before-unmark order.
Do not replace proven other-process WIN32 movement with UI convenience APIs.
Conflicting operations are rejected as busy; failures must remain visible.
See [architecture](ARCHITECTURE.md) for the full invariants and disconnect policy.

## Pull requests and dependencies

Explain the change and how it was checked; include real, privacy-safe screenshots
for visual work. Name skipped native scenarios instead of claiming full parity.
Keep unrelated cleanup out of the diff. Update the canonical usage/architecture
docs when behavior changes; the [site build](docs/SITE.md) renders them directly.

C# 14, SDK, NuGet locks and local tools are pinned. Dependabot proposes weekly
Actions/NuGet changes; inspect migration notes, regenerate affected locks,
and require CI. A dependency PR is not permission to change runtime semantics.

Product versions come from release tags. Do not rewrite historical assets,
move tags or publish a release as part of an ordinary PR. Follow
[release engineering](RELEASE.md); maintainers authorize release operations.
CODEOWNERS requests review but does not itself enable branch protection.
