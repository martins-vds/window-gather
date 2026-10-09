param(
    [Parameter(Mandatory)][string]$Tag,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidateSet('All', 'Build', 'ValidateBuild', 'Package')][string]$Stage = 'All',
    [string]$ExpectedCommit = '',
    [string]$OutputRoot = 'artifacts\releases',
    [switch]$SigningRequired
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$original = Get-Location
try {
    Set-Location (Split-Path $PSScriptRoot -Parent)
    $version = Get-ReleaseVersion $Tag
    $commit = Invoke-ReleaseGit rev-parse HEAD
    if ($ExpectedCommit -and $commit -cne $ExpectedCommit) { throw 'Checkout differs from the requested release source.' }
    if (Invoke-ReleaseGit status --porcelain --untracked-files=normal) { throw 'Release packaging requires a clean committed checkout.' }
    $tags = @(Invoke-ReleaseGit tag --list $Tag)
    if ($tags.Count -gt 0 -and (Invoke-ReleaseGit rev-parse "$Tag^{commit}") -cne $commit) {
        throw 'Existing release tag points to different source.'
    }
    $root = [IO.Path]::GetFullPath((Join-Path $OutputRoot "$Tag\$Runtime"))
    $publish = Join-Path $root 'publish'
    $executable = Join-Path $publish 'WindowGather.WinUI.exe'
    $buildFile = Join-Path $root 'build-manifest.json'
    if ($Stage -in @('All', 'Build')) {
        if (Test-Path -LiteralPath $root) { throw "Build output already exists: $root" }
        & .\scripts\Publish.ps1 -Format SingleFile -Runtime $Runtime -Tag $Tag -Commit $commit -OutputDirectory $publish
        $assembly = [IO.Path]::GetFullPath("WindowGather.WinUI\bin\Release\net10.0-windows10.0.19041.0\$Runtime\WindowGather.WinUI.dll")
        Assert-ReleaseMetadata $assembly $executable $version $commit $Runtime
        Assert-ReleaseSignature $executable $false
        [ordered]@{
            tag = $Tag; version = $version.Version; assemblyVersion = $version.AssemblyVersion
            runtimeIdentifier = $Runtime; commit = $commit; unsignedSha256 = Get-ReleaseHash $executable
        } | ConvertTo-Json | Set-Content -LiteralPath $buildFile -Encoding utf8
        if ($Stage -eq 'Build') { Write-Output "Verified build: $root"; return }
    }
    $build = Get-Content -LiteralPath $buildFile -Raw | ConvertFrom-Json
    if ($build.tag -cne $Tag -or $build.version -cne $version.Version -or
        $build.assemblyVersion -cne $version.AssemblyVersion -or $build.commit -cne $commit -or
        $build.runtimeIdentifier -cne $Runtime) { throw 'Build payload identity mismatch.' }
    Assert-ReleaseExecutable $executable $version $Runtime
    if ($Stage -eq 'ValidateBuild' -or -not $SigningRequired) {
        if ((Get-ReleaseHash $executable) -cne $build.unsignedSha256) { throw 'Build payload bytes changed before signing/packaging.' }
    }
    if ($Stage -eq 'ValidateBuild') { Assert-ReleaseSignature $executable $false; return }
    Assert-ReleaseSignature $executable ([bool]$SigningRequired)
    $package = Join-Path $root 'portable'
    if (Test-Path -LiteralPath $package) { throw "Package already exists: $package" }
    New-Item -ItemType Directory -Path $package | Out-Null
    Copy-Item -LiteralPath $executable -Destination $package
    $usage = Get-Content -LiteralPath .\WindowGather\README.md -Raw
    $usage = [regex]::Replace($usage, '\A# [^\r\n]+', "# Window Gather $Tag ($Runtime)")
    $usage | Set-Content -LiteralPath (Join-Path $package 'README.md') -Encoding utf8
    $version.Version | Set-Content -LiteralPath (Join-Path $package 'VERSION') -Encoding ascii
    $sourceArchive = Join-Path $package 'source.zip'
    Invoke-ReleaseGit archive --format=zip "--output=$sourceArchive" $commit
    $exeHash = Get-ReleaseHash (Join-Path $package 'WindowGather.WinUI.exe')
    $sourceHash = Get-ReleaseHash $sourceArchive
    [ordered]@{
        tag = $Tag; version = $version.Version; assemblyVersion = $version.AssemblyVersion
        component = 'Window Gather'; runtimeIdentifier = $Runtime; commit = $commit
        authenticodeSigned = [bool]$SigningRequired
        signingProvider = $(if ($SigningRequired) { 'Azure Artifact Signing' } else { 'none' })
        executableSha256 = $exeHash; sourceArchiveSha256 = $sourceHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'release-manifest.json') -Encoding utf8
    "$exeHash  WindowGather.WinUI.exe`n$sourceHash  source.zip" |
        Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding ascii
    $architecture = $Runtime.Substring(4)
    $zip = Join-Path $root "window-gather-windows-$architecture-$Tag.zip"
    $timestamp = [DateTimeOffset]::Parse((Invoke-ReleaseGit show -s --format=%cI $commit))
    New-ReleaseZip $package $zip $timestamp
    $null = Assert-ReleaseArchive $zip $version $commit $Runtime
    $zipHash = Get-ReleaseHash $zip
    "$zipHash  $([IO.Path]::GetFileName($zip))" |
        Set-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Package: $zip"
    Write-Output "Source: $commit"
    Write-Output "EXE SHA256: $exeHash"
    Write-Output "ZIP SHA256: $zipHash"
}
finally { Set-Location $original }
