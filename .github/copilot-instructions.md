# Window Gather contributor instructions

## Scope and workflow

- Read `CONTRIBUTING.md`, `ARCHITECTURE.md` and relevant code before editing.
  Work in this session's checkout; preserve unrelated/uncommitted changes.
- Use strict TDD for code: reproduce with a failing test, implement the smallest
  complete fix, then refactor with tests green. Integrate tests throughout.
  Keep changes focused and update directly related documentation.
- Use Windows paths and PowerShell 7. Preserve C# 14, `global.json`, checked-in
  package locks and `.config\dotnet-tools.json`; do not upgrade them to bypass errors.
- Do not commit, push, merge, tag, publish, install software or change Windows/cloud
  settings unless authorized. Include the required Copilot co-author trailer when
  committing. Read `RELEASE.md` before release work; never rewrite historical assets.

## Boundaries and safety

- Domain: BCL-only portable `net10.0`. Application depends on Domain.
  Infrastructure.Windows owns native/storage adapters. Presentation depends inward
  on Application/Domain and Toolkit MVVM; concrete adapters belong in WinUI composition.
- Preserve physical monitor identity, strict window identity, borrowed-only restore,
  destination-resident exclusion, durable schema-1 recovery, numeric shortcut values,
  save-before-move and persist-before-unmark ordering.
- Keep the shared operation gate: conflicting user requests are busy, not silently
  queued. Blocking native/storage work stays off the UI thread. Failures remain visible.
- Normal production app launches operate on the real desktop/settings. Use fake
  desktop services and injected native APIs for routine tests and mutation.
  Never gather user windows, change primary/startup settings, overwrite recovery or
  stop/replace an installed app as a test. Real native checks need an appropriate
  interactive desktop and applicable permission; move only owned disposable windows.
  Do not run desktop-operation tests on hosted CI. Follow the isolated real-XAML
  test/preview recipe in `CONTRIBUTING.md` when UI validation is needed.

## Build and fast verification

From the repository root:

```powershell
dotnet test .\WindowGather.Core.Tests\WindowGather.Core.Tests.csproj -c Release
dotnet run --project .\WindowGather.Infrastructure.Windows.Tests\WindowGather.Infrastructure.Windows.Tests.csproj -c Release
.\scripts\Verify.ps1
```

Use focused tests during TDD, then the smallest relevant broader checks.
`Verify.ps1` restores locked dependencies/tools, checks architecture/release/icon
contracts, runs safe infrastructure and portable tests, collects fresh Cobertura
coverage, enforces the existing quality gate and builds WinUI/legacy frontends.
Docs-only changes do not require mutation/builds; run `.\scripts\Test-Site.ps1`
when canonical/site documentation or links change.

## Mutation testing: Stryker.NET (not "striker")

Run the applicable Domain/Application mutation configuration after changes to
their behavior or tests; run both when shared behavior/boundaries change.
Use only portable tests with fake desktop services. From the root:

```powershell
dotnet tool restore
Push-Location .\WindowGather.Core.Tests
try {
    dotnet stryker --config-file .\stryker-domain.json --output ..\artifacts\mutation-domain
    if ($LASTEXITCODE -ne 0) { throw 'Domain mutation failed.' }
    dotnet stryker --config-file .\stryker-application.json --output ..\artifacts\mutation-application
    if ($LASTEXITCODE -ne 0) { throw 'Application mutation failed.' }
} finally { Pop-Location }
# Full existing verification plus both mutation gates:
.\scripts\Verify.ps1 -Mutation
```

Choose the applicable commands, not both recipes redundantly. Inspect the HTML/JSON
reports and meaningful survivors/uncovered mutants. Add behavioral assertions;
do not weaken the checked-in 80% break thresholds, globally exclude logic/equality
mutants, or substitute real desktop operations. Report the actual score, failures
and justified equivalent mutants; compilation errors/timeouts are not new tests.

## CRAP analysis: use martins-vds/crap4csharp

Use https://github.com/martins-vds/crap4csharp, not a guessed NuGet distribution.
At execution time resolve the **latest stable GitHub Release**, read its README
and release notes, and download its actual release asset. Do not hardcode an old
version. The verified contract at this update is a `Microsoft.Crap4CSharp.<version>.nupkg`
asset, package ID `Microsoft.Crap4CSharp`, command `crap4csharp`, portable AnyCPU,
framework-dependent .NET 8, Windows/Linux x64/ARM64. Window Gather remains Windows/
.NET 10: the tool additionally needs a matching-architecture .NET 8 runtime.
Do not silently install runtimes or force major-runtime roll-forward.

After authorization to acquire/install/run the tool, this PowerShell recipe
downloads the latest stable package and verifies its GitHub asset SHA-256 when
provided. Use an isolated local tool/feed, not a global installation:

```powershell
$ErrorActionPreference = 'Stop'
$releaseJson = gh api repos/martins-vds/crap4csharp/releases/latest
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve latest stable crap4csharp release.' }
$release = $releaseJson | ConvertFrom-Json
if ($release.draft -or $release.prerelease) { throw 'Expected a stable release.' }
$assets = @($release.assets | Where-Object name -Match '^Microsoft\.Crap4CSharp\.\d+\.\d+\.\d+\.nupkg$')
if ($assets.Count -ne 1) { throw 'Release asset contract changed; inspect upstream instructions.' }
$asset = $assets[0]
$version = $asset.name -replace '^Microsoft\.Crap4CSharp\.', '' -replace '\.nupkg$', ''
$root = Join-Path (Get-Location) ('artifacts\crap4csharp-' + [guid]::NewGuid().ToString('N'))
$feed = Join-Path $root 'feed'
$toolPath = Join-Path $root 'tool'
$null = New-Item -ItemType Directory -Path $feed
gh release download $release.tag_name --repo martins-vds/crap4csharp --pattern $asset.name --dir $feed
if ($LASTEXITCODE -ne 0) { throw 'Release package download failed.' }
$package = Join-Path $feed $asset.name
if ($asset.digest) {
    if ($asset.digest -notmatch '^sha256:([0-9a-fA-F]{64})$') { throw 'Unsupported asset digest; inspect upstream.' }
    if ((Get-FileHash $package -Algorithm SHA256).Hash -ine $Matches[1]) { throw 'Package checksum mismatch.' }
} else {
    throw 'No GitHub SHA-256 digest provided; inspect release checksum assets and verify before installing.'
}
$config = Join-Path $root 'NuGet.Config'
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content $config
dotnet tool install Microsoft.Crap4CSharp --version $version --tool-path $toolPath --configfile $config --add-source $feed
if ($LASTEXITCODE -ne 0) { throw 'Local tool installation failed.' }
$crap = Join-Path $toolPath 'crap4csharp.exe'
& $crap --help
if ($LASTEXITCODE -ne 0) { throw 'Tool help failed; check runtime and release compatibility.' }
```

Record the resolved tag/URL, digest and commands/results. If upstream changes the
asset/runtime/interface, adapt from its actual docs/help; never guess flags.
No credentials, source, coverage or reports may be uploaded to third-party services.

### Analyze this repository correctly

Run from the repository root, **one owning project per invocation**. There is no
`src` tree here: bare invocation, directory arguments and `--changed` default to
`src` selection and can misleadingly select nothing. Pass explicit C# file paths
and the real referencing VSTest/coverlet test project:

```powershell
# After obtaining $crap above; repeat separately for each affected owner.
$files = @(Get-ChildItem .\WindowGather.Application -Filter *.cs -File | ForEach-Object FullName)
if ($files.Count -eq 0) { throw 'No source files selected.' }
$options = @('--test-project', '.\WindowGather.Core.Tests\WindowGather.Core.Tests.csproj',
             '--framework', 'net10.0', '--crap-threshold', '8')
& $crap @files @options --dry-run
if ($LASTEXITCODE -ne 0) { throw 'CRAP selection/runner preflight failed.' }
& $crap @files @options *> (Join-Path $root 'application-report.txt')
$code = $LASTEXITCODE
if ($code -ne 0) { throw "CRAP analysis failed (exit $code); inspect the report." }
# For Domain, use WindowGather.Domain instead and a separate domain-report.txt.
# Add nested source files explicitly if introduced; exclude bin/obj/generated files.
```

The tool itself collects fresh **Cobertura** through `dotnet test --collect:"XPlat
Code Coverage"` and validates native TRX diagnostics. Do not pass the existing
ReportGenerator XML or invent a coverage-input flag. It deletes root `coverage\`
before collecting; inspect/preserve any existing content there first, do not run
analyses concurrently, and preserve each report before the next run replaces coverage.
Default test selection excludes `type=IntegrationTests` and includes untagged
portable tests; never select the interactive console harness as a coverage suite.

Exit 0 means threshold respected, 1 means fatal/usage/coverage failure, 2 means
strict unrounded maximum CRAP exceeds the specified threshold (default 8).
Do not suppress nonzero exits, raise thresholds just to pass, or claim a passing
baseline without measuring. Investigate `N/A`, empty selection, absent members and
all-`N/A` results: those can exit 0 without meaningful analysis. Constructors are
excluded by this tool. Preserve the existing `Verify.ps1` quality gate too;
its ReportGenerator-based metrics are **not a substitute** for crap4csharp and
its historical maximum is not an established crap4csharp baseline. Line coverage,
branch coverage, mutation score and CRAP are distinct evidence.
