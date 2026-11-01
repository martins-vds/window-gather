param(
    [ValidateSet('Folder', 'SingleFile')][string]$Format = 'Folder',
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Tag = '',
    [string]$Commit = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$original = Get-Location
try {
    Set-Location (Split-Path $PSScriptRoot -Parent)
    $single = $Format -eq 'SingleFile'
    $output = Join-Path 'artifacts' ("publish-$Runtime-" + $Format.ToLowerInvariant())
    if ($OutputDirectory) { $output = $OutputDirectory }
    $versionProperties = @()
    $lock = "packages.publish-$Runtime-$Format.lock.json"
    if ($Tag) {
        . .\scripts\Release.Common.ps1
        $releaseVersion = Get-ReleaseVersion $Tag
        if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw 'A full source commit is required for tag publishing.' }
        $versionProperties = @("-p:ReleaseTag=$Tag", "-p:Version=$($releaseVersion.Version)",
            "-p:PackageVersion=$($releaseVersion.Version)", "-p:AssemblyVersion=$($releaseVersion.AssemblyVersion)",
            "-p:FileVersion=$($releaseVersion.AssemblyVersion)", "-p:InformationalVersion=$($releaseVersion.Version)",
            '-p:IncludeSourceRevisionInInformationalVersion=false', "-p:RepositoryCommit=$Commit", '-p:ContinuousIntegrationBuild=true')
    }
    $properties = @(
        '-p:UiTestBuild=false', '-p:WindowsPackageType=None', '-p:EnableMsixTooling=true', '-p:WindowsAppSDKSelfContained=true',
        '-p:SelfContained=true', "-p:PublishSingleFile=$single", "-p:IncludeAllContentForSelfExtract=$single",
        "-p:IncludeNativeLibrariesForSelfExtract=$single",
        "-p:NuGetLockFilePath=$lock", '-p:DebugType=none', '-p:DebugSymbols=false'
    ) + $versionProperties
    & dotnet restore .\WindowGather.WinUI\WindowGather.WinUI.csproj -r $Runtime --locked-mode --nologo @properties
    if ($LASTEXITCODE -ne 0) { throw "Locked publish restore failed with exit code $LASTEXITCODE." }
    & dotnet publish .\WindowGather.WinUI\WindowGather.WinUI.csproj -c Release -r $Runtime --self-contained true `
        --no-restore -o $output --nologo @properties
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
