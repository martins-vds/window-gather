param([string]$OutputDirectory = 'artifacts\site')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build-Site.ps1') -OutputDirectory $OutputDirectory
$output = Join-Path $root $OutputDirectory
$pages = @(Get-ChildItem -LiteralPath $output -Filter '*.html' -File)
if ($pages.Count -ne 6) { throw "Expected 6 pages, found $($pages.Count)." }
$checks = 0
foreach ($page in $pages) {
    $html = Get-Content -LiteralPath $page.FullName -Raw
    if ($html -notmatch '<html lang="en">' -or $html -notmatch 'name="viewport"') { throw "Missing page accessibility metadata: $($page.Name)." }
    foreach ($match in [regex]::Matches($html, '(?:href|src)="([^"]+)"')) {
        $link = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
        if ($link -match '^(https?://|mailto:)') { continue }
        if ($link.StartsWith('/')) { throw "Root-absolute link breaks the project Pages path: $link." }
        $parts = $link.Split('#', 2)
        $target = if ($parts[0] -eq '') { $page.FullName } elseif ($parts[0] -eq './') { Join-Path $output 'index.html' } else { Join-Path $output $parts[0] }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Broken generated link $link in $($page.Name)." }
        if ($parts.Count -eq 2) {
            $body = Get-Content -LiteralPath $target -Raw
            if ($body -notmatch ('id="' + [regex]::Escape($parts[1]) + '"')) { throw "Missing anchor $link in $($page.Name)." }
        }
        $checks++
    }
}
if ($checks -lt 30) { throw "Suspiciously empty link validation ($checks)." }
Write-Output "Passed $checks local asset/anchor/project-base-path checks across $($pages.Count) pages."
