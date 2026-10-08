param(
    [ValidateSet('Folder', 'SingleFile')][string]$Format = 'Folder',
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$original = Get-Location
try {
    Set-Location (Split-Path $PSScriptRoot -Parent)
    $single = $Format -eq 'SingleFile'
    $output = Join-Path 'artifacts' ("publish-$Runtime-" + $Format.ToLowerInvariant())
    & dotnet publish .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release -r $Runtime --self-contained true `
        -p:UiTestBuild=false -p:WindowsPackageType=None -p:EnableMsixTooling=true -p:WindowsAppSDKSelfContained=true `
        "-p:PublishSingleFile=$single" "-p:IncludeAllContentForSelfExtract=$single" `
        "-p:IncludeNativeLibrariesForSelfExtract=$single" `
        "-p:NuGetLockFilePath=obj\packages.publish-$Runtime-$Format.lock.json" `
        -p:DebugType=none -p:DebugSymbols=false -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
    if ($single) {
        $files = @(Get-ChildItem -LiteralPath $output -File)
        if ($files.Count -ne 1 -or $files[0].Name -ne 'WindowGather.WinUI.exe') {
            throw "Single-file output is not exactly one distributable executable. Inspect $output."
        }
    }
    Write-Output "Published to $output"
}
finally { Set-Location $original }
