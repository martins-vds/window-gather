$ErrorActionPreference = 'Stop'
$original = Get-Location
try {
    Set-Location (Split-Path $PSScriptRoot -Parent)
    $status = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the Git checkout.' }
    if ($status) { throw 'Release packaging requires a clean committed checkout.' }
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the release source commit.' }
    $version = ([xml](Get-Content -LiteralPath .\Directory.Build.props -Raw)).Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $version" }
    $manifest = [xml](Get-Content -LiteralPath .\WindowGather.WinUI\app.manifest -Raw)
    if ($manifest.assembly.assemblyIdentity.version -ne "$version.0") {
        throw 'Native manifest version differs from the release version.'
    }
    $name = "WindowGather-$version-win-x64"
    $release = Join-Path 'artifacts\releases' $name
    if (Test-Path -LiteralPath $release) { throw "Release directory already exists: $release" }
    & .\scripts\Publish.ps1 -Format SingleFile -Runtime win-x64
    $published = Get-Item -LiteralPath .\artifacts\publish-win-x64-singlefile\WindowGather.WinUI.exe
    if ($published.VersionInfo.FileVersion -ne "$version.0" -or
        $published.VersionInfo.ProductVersion.Split('+')[0] -ne $version) {
        throw 'Published executable version differs from the release version.'
    }
    $package = Join-Path $release 'portable'
    New-Item -ItemType Directory -Path $package | Out-Null
    $executable = Join-Path $package 'WindowGather.WinUI.exe'
    Copy-Item -LiteralPath $published.FullName -Destination $executable
    Copy-Item -LiteralPath .\WindowGather\README.md -Destination (Join-Path $package 'README.md')
    $sourceArchive = Join-Path $release 'source.zip'
    try {
        & git archive --format=zip "--output=$sourceArchive" $commit
        if ($LASTEXITCODE -ne 0) { throw 'Cannot archive the committed release source.' }
        Expand-Archive -LiteralPath $sourceArchive -DestinationPath (Join-Path $package 'source')
    }
    finally {
        if (Test-Path -LiteralPath $sourceArchive) { Remove-Item -LiteralPath $sourceArchive }
    }
    $exeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    [ordered]@{
        Version = $version
        SourceCommit = $commit
        RuntimeIdentifier = 'win-x64'
        Distribution = 'Self-contained single executable with runtime extraction'
        Executable = 'WindowGather.WinUI.exe'
        ExecutableSha256 = $exeHash
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'RELEASE.json') -Encoding utf8
    "$exeHash  WindowGather.WinUI.exe" |
        Set-Content -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -Encoding ascii
    $zip = Join-Path $release "$name-portable.zip"
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    @("$zipHash  $name-portable.zip", "$exeHash  portable\WindowGather.WinUI.exe") |
        Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
    Write-Output "Release: $((Get-Item -LiteralPath $release).FullName)"
    Write-Output "Source: $commit"
    Write-Output "EXE SHA256: $exeHash"
    Write-Output "ZIP SHA256: $zipHash"
}
finally { Set-Location $original }
