param(
    [switch]$Native,
    [switch]$LegacyUi,
    [switch]$Mutation
)
$ErrorActionPreference = 'Stop'

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $args failed with exit code $LASTEXITCODE." }
}

$original = Get-Location
try {
    Set-Location (Split-Path $PSScriptRoot -Parent)
    & .\scripts\VerifyDependencies.ps1
    & .\scripts\Test-Release.ps1
    Invoke-Dotnet tool restore
    Invoke-Dotnet restore .\WindowGather.slnx --locked-mode --nologo
    $run = Join-Path 'artifacts' ('coverage-' + [guid]::NewGuid().ToString('N'))
    Invoke-Dotnet test .\WindowGather.Core.Tests\WindowGather.Core.Tests.csproj -c Release --nologo '--collect:XPlat Code Coverage' --results-directory $run
    $coverage = @(Get-ChildItem -LiteralPath $run -Recurse -Filter coverage.cobertura.xml)
    if ($coverage.Count -ne 1) { throw "Expected exactly one fresh Cobertura report, found $($coverage.Count)." }
    $report = Join-Path $run 'report'
    Invoke-Dotnet reportgenerator "-reports:$($coverage[0].FullName)" "-targetdir:$report" '-reporttypes:Html;Xml;TextSummary'
    Invoke-Dotnet run --project .\WindowGather.Quality\WindowGather.Quality.csproj -c Release -- $report $coverage[0].FullName
    Invoke-Dotnet build .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release '-p:UiTestBuild=false' --nologo
    Invoke-Dotnet build .\WindowGather\WindowGather.csproj -c Release --nologo
    if ($Native -or $LegacyUi) {
        $selectors = @()
        if ($Native) { $selectors += '--native' }
        if ($LegacyUi) { $selectors += '--ui' }
        Invoke-Dotnet run --project .\WindowGather.Tests\WindowGather.Tests.csproj -c Release -- @selectors
    }
    if ($Mutation) {
        Set-Location .\WindowGather.Core.Tests
        Invoke-Dotnet stryker --config-file .\stryker-domain.json --output ..\artifacts\mutation-domain
        Invoke-Dotnet stryker --config-file .\stryker-application.json --output ..\artifacts\mutation-application
    }
}
finally { Set-Location $original }
