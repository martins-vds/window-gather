param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$Repository,
    [string]$AssetDirectory = 'release-assets',
    [switch]$AllowNewTag,
    [switch]$AutomaticToken,
    [string]$DefaultBranch = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Release.Common.ps1')
$version = Get-ReleaseVersion $Tag
if ($Commit -cnotmatch '^[0-9a-f]{40}$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw 'Invalid source/repository identity.'
}
if (-not $env:GH_TOKEN) { throw 'GitHub release publication requires GH_TOKEN.' }
if ((Invoke-ReleaseGit rev-parse HEAD) -cne $Commit) { throw 'Release job checkout differs from artifact source.' }
$tagCommit = Get-RemoteReleaseCommit $Tag
if ($null -eq $tagCommit -and -not $AllowNewTag) {
    throw 'Release tag is missing; new tags require an explicitly prepared manual release.'
}
if ($null -ne $tagCommit -and $tagCommit -cne $Commit) {
    throw 'Remote tag moved or differs from artifact source.'
}
if ($null -eq $tagCommit -and $AutomaticToken) { Assert-AutomaticTagPermission $Commit $DefaultBranch }
$assets = @()
foreach ($runtime in @('win-x64', 'win-arm64')) {
    $path = Join-Path $AssetDirectory "window-gather-windows-$($runtime.Substring(4))-$Tag.zip"
    $null = Assert-ReleaseArchive $path $version $Commit $runtime
    $assets += Get-Item -LiteralPath $path
}
foreach ($file in Get-ChildItem -LiteralPath $AssetDirectory -File) {
    if ($file.Name -cnotin @($assets.Name) -and $file.Name -cne 'SHA256SUMS.txt') { throw 'Unexpected release asset.' }
}
$checksums = Join-Path $AssetDirectory 'SHA256SUMS.txt'
$assets | ForEach-Object { "$(Get-ReleaseHash $_.FullName)  $($_.Name)" } |
    Set-Content -LiteralPath $checksums -Encoding ascii
$assets += Get-Item -LiteralPath $checksums
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = if ($env:GITHUB_API_URL) { $env:GITHUB_API_URL } else { 'https://api.github.com' }
$repoResult = Invoke-WebRequest -Uri "$api/repos/$Repository" -Headers $headers -SkipHttpErrorCheck
if ($repoResult.StatusCode -ne 200) { throw "Cannot authenticate/read release repository: HTTP $($repoResult.StatusCode)." }
$response = Invoke-WebRequest -Uri "$api/repos/$Repository/releases/tags/$Tag" -Headers $headers -SkipHttpErrorCheck
if ($response.StatusCode -eq 200) {
    if ($null -eq $tagCommit) { throw 'Existing release has no source tag; automatic repair is not permitted.' }
    $release = $response.Content | ConvertFrom-Json
    if ($release.tag_name -cne $Tag -or $release.draft -or $release.prerelease) { throw 'Existing release has incompatible state.' }
    if (@($release.assets).Count -ne $assets.Count) { throw 'Existing release assets differ; immutable releases are never clobbered.' }
    foreach ($file in $assets) {
        $existing = @($release.assets | Where-Object { $_.name -ceq $file.Name })
        if ($existing.Count -ne 1) { throw "Existing release is missing $($file.Name)." }
        $download = Join-Path $AssetDirectory "existing-$($file.Name)"
        try {
            $downloadHeaders = $headers.Clone()
            $downloadHeaders.Accept = 'application/octet-stream'
            $downloadResponse = Invoke-WebRequest -Uri "$api/repos/$Repository/releases/assets/$($existing[0].id)" `
                -Headers $downloadHeaders -SkipHttpErrorCheck -OutFile $download -PassThru
            if ($downloadResponse.StatusCode -ne 200) { throw "Cannot verify existing asset: HTTP $($downloadResponse.StatusCode)." }
            if ((Get-ReleaseHash $download) -cne (Get-ReleaseHash $file.FullName)) {
                throw "Existing asset $($file.Name) has different bytes. Use a new tag; assets are immutable."
            }
        }
        finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download } }
    }
    Write-Output "Release $Tag already contains these exact verified assets; no changes made."
}
elseif ($response.StatusCode -eq 404) {
    if ($null -eq $tagCommit) {
        Invoke-ReleaseGit push origin "${Commit}:refs/tags/$Tag"
        if ((Get-RemoteReleaseCommit $Tag) -cne $Commit) { throw 'New release tag differs from verified artifact source.' }
    }
    & gh release create $Tag @($assets.FullName) --repo $Repository --verify-tag --target $Commit `
        --title "Window Gather $Tag" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw 'GitHub release creation failed; no update/clobber fallback is permitted.' }
}
else { throw "Cannot inspect GitHub release: HTTP $($response.StatusCode). No release mutation attempted." }
