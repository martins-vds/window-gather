param([string]$OutputDirectory = 'artifacts\site')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$sources = [ordered]@{
    'WindowGather\README.md' = 'usage.html'
    'CONTRIBUTING.md' = 'contributing.html'
    'ARCHITECTURE.md' = 'architecture.html'
    'RELEASE.md' = 'releases.html'
    'SECURITY.md' = 'security.html'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\site\index.html'), (Join-Path $root 'docs\site\style.css') -Destination $output
New-Item -ItemType Directory -Path (Join-Path $output 'images') -Force | Out-Null
Get-ChildItem (Join-Path $root 'docs\images') -File | Copy-Item -Destination (Join-Path $output 'images')
foreach ($entry in $sources.GetEnumerator()) {
    $source = Join-Path $root $entry.Key
    $html = (ConvertFrom-Markdown -Path $source).Html
    $html = [regex]::Replace($html, '(?<prefix>(?:href|src)=")(?<link>[^"]+)"', {
        param($match)
        $link = [Net.WebUtility]::HtmlDecode($match.Groups['link'].Value)
        if ($link -match '^(https?://|mailto:|#)') { return $match.Value }
        $parts = $link.Split('#', 2)
        $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path $source -Parent) $parts[0]))
        $relative = [IO.Path]::GetRelativePath($root, $resolved).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $resolved)) { throw "Broken source link '$link' in $($entry.Key)." }
        if ($sources.Contains($relative)) { $target = $sources[$relative] }
        elseif ($relative -eq 'README.md') { $target = 'index.html' }
        elseif ($relative.StartsWith('docs\images\')) { $target = 'images/' + [IO.Path]::GetFileName($relative) }
        else { $target = 'https://github.com/martins-vds/window-gather/blob/main/' + $relative.Replace('\', '/') }
        if ($parts.Count -eq 2) { $target += '#' + $parts[1] }
        return $match.Groups['prefix'].Value + [Net.WebUtility]::HtmlEncode($target) + '"'
    })
    $title = [Net.WebUtility]::HtmlEncode(([regex]::Match($html, '<h1[^>]*>(.*?)</h1>')).Groups[1].Value)
    $page = @"
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>$title — Window Gather</title><link rel="stylesheet" href="style.css"></head>
<body><a class="skip" href="#main">Skip to content</a>
<header class="site-header"><a class="brand" href="./">Window Gather</a><nav aria-label="Main navigation">
<a href="usage.html">User guide</a><a href="contributing.html">Developers</a><a href="https://github.com/martins-vds/window-gather">GitHub</a></nav></header>
<main id="main"><article class="document">$html</article></main>
<footer><p>Rendered from the canonical repository Markdown. <a href="https://github.com/martins-vds/window-gather/blob/main/$($entry.Key.Replace('\', '/'))">Edit this source</a>.</p></footer></body></html>
"@
    Set-Content -LiteralPath (Join-Path $output $entry.Value) -Value $page -Encoding utf8
}
Set-Content -LiteralPath (Join-Path $output '.nojekyll') -Value '' -Encoding utf8
Write-Output "Site built at $output"
