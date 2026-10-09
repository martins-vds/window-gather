$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$global:WindowGatherReleaseTestState = @{ checks = 0 }
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Release test failed: $Message" }
    $global:WindowGatherReleaseTestState.checks++
}
function Reject([scriptblock]$Action, [string]$Message, [string]$ExpectedError = '') {
    $failed = $false
    try { & $Action | Out-Null } catch {
        if ($ExpectedError -and $_.Exception.Message -cne $ExpectedError) { throw }
        $failed = $true
    }
    Check $failed $Message
}
foreach ($tag in @('v0.0.0', 'v2.1.3', 'v65534.65534.65534')) {
    $version = Get-ReleaseVersion $tag
    Check ($version.Version -ceq $tag.Substring(1) -and $version.AssemblyVersion -ceq "$($tag.Substring(1)).0") $tag
}
foreach ($tag in @('', '2.1.3', 'v2.1', 'v2', 'v2.1.3.4', 'v2.1.3-beta', 'V2.1.3', 'v01.2.3',
    'v1.02.3', 'v1.2.03', 'v-1.2.3', 'v65535.0.0', 'v0.65535.0', 'v0.0.65535',
    'v4294967296.0.0', 'v999999999999999999999999.1.2', "v2.1.3`n", 'v2.1.3 ')) {
    Reject { Get-ReleaseVersion $tag } $tag
}
foreach ($enabled in @('', 'false', 'true')) {
    Check ((Get-SigningPolicy auto $enabled) -eq ($enabled -ceq 'true')) "auto/$enabled"
    Check (Get-SigningPolicy enabled $enabled) "enabled/$enabled"
    Check (-not (Get-SigningPolicy disabled $enabled)) "disabled/$enabled"
}
foreach ($mode in @('auto', 'enabled', 'disabled')) {
    Reject { Get-SigningPolicy $mode 'TRUE' } 'invalid repository policy'
}
Reject { Get-SigningPolicy 'other' '' } 'invalid signing mode'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$commit = Invoke-ReleaseGit -C $repositoryRoot rev-parse HEAD
$temporary = Join-Path $repositoryRoot ('artifacts\release-contract-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $version = Get-ReleaseVersion 'v3.4.5'
    $timestamp = [DateTimeOffset]'2026-01-01T00:00:00Z'
    foreach ($runtime in @('win-x64', 'win-arm64')) {
        $package = Join-Path $temporary $runtime
        New-Item -ItemType Directory -Path $package | Out-Null
        'Owned synthetic bytes, never executed.' | Set-Content (Join-Path $package 'WindowGather.WinUI.exe')
        '# Synthetic package' | Set-Content (Join-Path $package 'README.md')
        $version.Version | Set-Content (Join-Path $package 'VERSION')
        $source = Join-Path $package 'source.zip'
        Invoke-ReleaseGit -C $repositoryRoot archive --format=zip "--output=$source" $commit
        $exeHash = Get-ReleaseHash (Join-Path $package 'WindowGather.WinUI.exe')
        $sourceHash = Get-ReleaseHash $source
        $manifest = [ordered]@{
            tag = $version.Tag; version = $version.Version; assemblyVersion = $version.AssemblyVersion
            commit = $commit; runtimeIdentifier = $runtime; component = 'Window Gather'
            authenticodeSigned = $false; signingProvider = 'none'
            executableSha256 = $exeHash; sourceArchiveSha256 = $sourceHash
        }
        $manifest | ConvertTo-Json | Set-Content (Join-Path $package 'release-manifest.json')
        "$exeHash  WindowGather.WinUI.exe`n$sourceHash  source.zip" | Set-Content (Join-Path $package 'SHA256SUMS.txt')
        $archive = Join-Path $temporary "window-gather-windows-$($runtime.Substring(4))-$($version.Tag).zip"
        New-ReleaseZip $package $archive $timestamp
        $verified = Assert-ReleaseArchive $archive $version $commit $runtime
        Check (-not $verified.authenticodeSigned) 'unsigned declaration'
        Reject { Assert-ReleaseArchive $archive $version ('a' * 40) $runtime } 'mismatched source commit'
        Reject { Assert-ReleaseArchive $archive (Get-ReleaseVersion v3.4.6) $commit $runtime } 'mismatched tag'
        Reject { Assert-ReleaseArchive $archive $version $commit 'win-other' } 'mismatched RID'
        $repeat = Join-Path $temporary "$runtime-repeat.zip"
        New-ReleaseZip $package $repeat $timestamp
        Check ((Get-ReleaseHash $archive) -ceq (Get-ReleaseHash $repeat)) 'repeatable ZIP timestamps/ordering'
        Remove-Item -LiteralPath $repeat
        'tampered executable' | Set-Content (Join-Path $package 'WindowGather.WinUI.exe')
        $bad = Join-Path $temporary "$runtime-bad.zip"
        New-ReleaseZip $package $bad $timestamp
        Reject { Assert-ReleaseArchive $bad $version $commit $runtime } 'changed executable bytes'
        Remove-Item -LiteralPath $bad
        'Owned synthetic bytes, never executed.' | Set-Content (Join-Path $package 'WindowGather.WinUI.exe')
        $sourceBytes = [IO.File]::ReadAllBytes($source)
        $sourceBytes[10] = $sourceBytes[10] -bxor 1
        [IO.File]::WriteAllBytes($source, $sourceBytes)
        $sourceHash = Get-ReleaseHash $source
        $manifest.sourceArchiveSha256 = $sourceHash
        $manifest | ConvertTo-Json | Set-Content (Join-Path $package 'release-manifest.json')
        "$exeHash  WindowGather.WinUI.exe`n$sourceHash  source.zip" | Set-Content (Join-Path $package 'SHA256SUMS.txt')
        New-ReleaseZip $package $bad $timestamp
        Reject { Assert-ReleaseArchive $bad $version $commit $runtime } 'forged source with consistent hashes and unchanged commit comment' 'Source archive differs from the committed tree.'
        Remove-Item -LiteralPath $bad
    }

    # Mock every network/native GitHub invocation; these checks never publish a release.
    $global:WindowGatherReleaseTestState.repoStatus = 200
    $global:WindowGatherReleaseTestState.releaseStatus = 404
    $global:WindowGatherReleaseTestState.remoteCommit = $commit
    $global:WindowGatherReleaseTestState.commit = $commit
    $global:WindowGatherReleaseTestState.temporary = $temporary
    $global:WindowGatherReleaseTestState.tag = $version.Tag
    $global:WindowGatherReleaseTestState.gitExecutable = (Get-Command git -CommandType Application | Select-Object -First 1).Source
    $global:WindowGatherReleaseTestState.ghCalls = 0
    $global:WindowGatherReleaseTestState.tamperDownload = $false
    $global:WindowGatherReleaseTestState.partialAssets = $false
    function git {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq '-C' -and $args[2] -eq 'archive') {
            & $global:WindowGatherReleaseTestState.gitExecutable @args
            return
        }
        if ($args[0] -eq 'rev-parse') {
            if ($args[1] -eq 'HEAD') { return $global:WindowGatherReleaseTestState.commit }
            return $global:WindowGatherReleaseTestState.remoteCommit
        }
        if ($args[0] -ne 'fetch') { throw "Unexpected mocked git invocation: $args" }
    }
    function gh {
        Check ($args -contains '--verify-tag' -and $args -contains $global:WindowGatherReleaseTestState.commit) 'create verifies tag/source'
        $global:WindowGatherReleaseTestState.ghCalls++
        $global:LASTEXITCODE = 0
    }
    function Invoke-WebRequest {
        param($Uri, $Headers, [switch]$SkipHttpErrorCheck, $OutFile, [switch]$PassThru)
        if ($Uri -match '/releases/assets/(\d+)$') {
            $asset = @(Get-ChildItem -LiteralPath $global:WindowGatherReleaseTestState.temporary -File | Sort-Object Name)[[int]$Matches[1]]
            Copy-Item -LiteralPath $asset.FullName -Destination $OutFile
            if ($global:WindowGatherReleaseTestState.tamperDownload) { Add-Content -LiteralPath $OutFile -Value 'changed bytes' }
            return [pscustomobject]@{ StatusCode = 200 }
        }
        if ($Uri -match '/releases/tags/') {
            $assets = @()
            $i = 0
            foreach ($file in Get-ChildItem -LiteralPath $global:WindowGatherReleaseTestState.temporary -File | Sort-Object Name) {
                $assets += @{ name = $file.Name; id = $i++ }
            }
            if ($global:WindowGatherReleaseTestState.partialAssets) { $assets = @($assets | Select-Object -Skip 1) }
            return [pscustomobject]@{ StatusCode = $global:WindowGatherReleaseTestState.releaseStatus; Content = (@{
                tag_name = $global:WindowGatherReleaseTestState.tag; draft = $false; prerelease = $false; assets = $assets
            } | ConvertTo-Json -Depth 5) }
        }
        return [pscustomobject]@{ StatusCode = $global:WindowGatherReleaseTestState.repoStatus }
    }
    $token = $env:GH_TOKEN
    $env:GH_TOKEN = 'synthetic-not-a-credential'
    try {
        $publisher = Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1'
        & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary
        Check ($global:WindowGatherReleaseTestState.ghCalls -eq 1) '404 creates exactly once'
        $global:WindowGatherReleaseTestState.releaseStatus = 200
        & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary
        Check ($global:WindowGatherReleaseTestState.ghCalls -eq 1) 'identical rerun makes no mutations'
        $global:WindowGatherReleaseTestState.tamperDownload = $true
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'existing changed bytes are never clobbered'
        $global:WindowGatherReleaseTestState.tamperDownload = $false
        $global:WindowGatherReleaseTestState.partialAssets = $true
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'partial releases are not silently repaired'
        $global:WindowGatherReleaseTestState.partialAssets = $false
        $global:WindowGatherReleaseTestState.releaseStatus = 500
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'unexpected API error is not absence'
        $global:WindowGatherReleaseTestState.releaseStatus = 403
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'release auth failure is not absence'
        $global:WindowGatherReleaseTestState.repoStatus = 403
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'repository auth failure'
        $global:WindowGatherReleaseTestState.repoStatus = 200
        $global:WindowGatherReleaseTestState.remoteCommit = 'a' * 40
        Reject { & $publisher -Tag $version.Tag -Commit $commit -Repository 'test/repository' -AssetDirectory $temporary } 'moved tag'
        Check ($global:WindowGatherReleaseTestState.ghCalls -eq 1) 'no mutation after auth/identity failures'
    }
    finally { $env:GH_TOKEN = $token; Remove-Item Function:\git, Function:\gh, Function:\Invoke-WebRequest }
    Write-Output "PASS $($global:WindowGatherReleaseTestState.checks) release version/signing/archive/source/publication contract checks."
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
    Remove-Variable -Name WindowGatherReleaseTestState -Scope Global
}
