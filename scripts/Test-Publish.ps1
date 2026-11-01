$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$global:WindowGatherPublishCalls = [Collections.Generic.List[object]]::new()
function dotnet {
    $global:WindowGatherPublishCalls.Add(@($args))
    $global:LASTEXITCODE = 0
}
try {
    foreach ($runtime in @('win-x64', 'win-arm64')) {
        foreach ($format in @('Folder', 'SingleFile')) {
            $global:WindowGatherPublishCalls.Clear()
            # Single-file output shape is checked against the existing owned publish directory.
            if ($format -eq 'SingleFile') {
                $output = Join-Path $root ('artifacts\publish-contract-' + [guid]::NewGuid().ToString('N'))
                New-Item -ItemType Directory -Path $output | Out-Null
                Set-Content -LiteralPath (Join-Path $output 'WindowGather.WinUI.exe') -Value 'Synthetic, never executed.'
            } else { $output = Join-Path $root 'artifacts\publish-contract-folder' }
            try {
                & (Join-Path $PSScriptRoot 'Publish.ps1') -Runtime $runtime -Format $format -OutputDirectory $output | Out-Null
                $calls = $global:WindowGatherPublishCalls
                if ($calls.Count -ne 2 -or $calls[0][0] -ne 'restore' -or $calls[1][0] -ne 'publish' -or
                    $calls[0] -notcontains '--locked-mode' -or $calls[1] -notcontains '--no-restore') {
                    throw 'Publishing must restore locked dependencies once and publish without restoring again.'
                }
                $lock = "-p:NuGetLockFilePath=packages.publish-$runtime-$format.lock.json"
                if ($calls[0] -notcontains $lock -or $calls[1] -notcontains $lock) { throw 'Publish lock identity differs from restore.' }
                foreach ($project in @('WindowGather.Domain', 'WindowGather.Application', 'WindowGather.Presentation',
                    'WindowGather.Infrastructure.Windows', 'WindowGather.WinUI')) {
                    if (-not (Test-Path -LiteralPath (Join-Path $root "$project\packages.publish-$runtime-$format.lock.json"))) {
                        throw "Missing checked-in publish graph for $project/$runtime/$format."
                    }
                }
                $global:WindowGatherPublishCalls.Clear()
                & (Join-Path $PSScriptRoot 'Publish.ps1') -Runtime $runtime -Format $format -OutputDirectory $output `
                    -Tag v9.8.7 -Commit ('a' * 40) | Out-Null
                if ($global:WindowGatherPublishCalls[0] -notcontains $lock -or
                    $global:WindowGatherPublishCalls[1] -notcontains $lock) {
                    throw 'Changing the release tag must not create a new unlocked dependency graph.'
                }
            }
            finally {
                if ($format -eq 'SingleFile') {
                    Remove-Item -LiteralPath (Join-Path $output 'WindowGather.WinUI.exe')
                    Remove-Item -LiteralPath $output
                }
            }
        }
    }
    Write-Output 'PASS locked publish restore contracts for x64/ARM64 and folder/single-file.'
}
finally {
    Remove-Item Function:\dotnet
    Remove-Variable WindowGatherPublishCalls -Scope Global
}
