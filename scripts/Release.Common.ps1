Set-StrictMode -Version Latest

function Get-ReleaseVersion([string]$Tag) {
    if ($Tag -cnotmatch '\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
        throw "Release tag must be a stable vMAJOR.MINOR.PATCH without leading zeros: $Tag"
    }
    foreach ($part in $Tag.Substring(1).Split('.')) {
        [uint32]$number = 0
        if (-not [uint32]::TryParse($part, [ref]$number) -or $number -gt 65534) {
            throw "Assembly version components must be between 0 and 65534: $Tag"
        }
    }
    [pscustomobject]@{ Tag = $Tag; Version = $Tag.Substring(1); AssemblyVersion = "$($Tag.Substring(1)).0" }
}

function Get-SigningPolicy([string]$Mode, [string]$RepositoryEnabled = '') {
    if ($RepositoryEnabled -cnotin @('', 'true', 'false')) {
        throw 'AZURE_ARTIFACT_SIGNING_ENABLED must be unset, true or false.'
    }
    switch -CaseSensitive ($Mode) {
        'auto' { return $RepositoryEnabled -ceq 'true' }
        'enabled' { return $true }
        'disabled' { return $false }
        default { throw 'Signing mode must be auto, enabled or disabled.' }
    }
}

function Invoke-ReleaseGit {
    $result = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $args failed ($LASTEXITCODE)." }
    return $result
}

function Get-ReleaseHash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Initialize-ReleaseInspection {
    if (-not ('WindowGather.ReleaseInspection' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'Release.Inspection.cs')
    }
}

function Assert-ReleaseMetadata([string]$Assembly, [string]$Executable, $Version, [string]$Commit, [string]$Runtime) {
    Initialize-ReleaseInspection
    $name = [Reflection.AssemblyName]::GetAssemblyName($Assembly)
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Assembly)
    if ($name.Version.ToString() -cne $Version.AssemblyVersion -or
        $info.FileVersion -cne $Version.AssemblyVersion -or $info.ProductVersion -cne $Version.Version) {
        throw "Managed assembly version mismatch: $Assembly"
    }
    $metadata = [WindowGather.ReleaseInspection]::AssemblyMetadata($Assembly)
    if ($metadata['RepositoryCommit'] -cne $Commit -or $metadata['ReleaseTag'] -cne $Version.Tag) {
        throw 'Managed assembly source identity differs from the release.'
    }
    Assert-ReleaseExecutable $Executable $Version $Runtime
}

function Assert-ReleaseExecutable([string]$Executable, $Version, [string]$Runtime) {
    Initialize-ReleaseInspection
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Executable)
    if ($info.FileVersion -cne $Version.AssemblyVersion -or $info.ProductVersion -cne $Version.Version) {
        throw 'Executable version mismatch.'
    }
    $machine = [WindowGather.ReleaseInspection]::Machine($Executable)
    $expectedMachine = switch ($Runtime) { 'win-x64' { 0x8664 }; 'win-arm64' { 0xAA64 }; default { throw 'Unsupported release RID.' } }
    if ($machine -ne $expectedMachine) { throw 'Executable architecture differs from its release RID.' }
    [xml]$manifest = [WindowGather.ReleaseInspection]::NativeManifest($Executable)
    if ($manifest.assembly.assemblyIdentity.version -cne $Version.AssemblyVersion -or
        $manifest.assembly.assemblyIdentity.name -cne 'WindowGather.WinUI') {
        throw 'Embedded native manifest differs from the release version.'
    }
}

function Assert-ReleaseSignature([string]$Executable, [bool]$Required) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Executable
    if (-not $Required) {
        if ($signature.Status -ne 'NotSigned') { throw 'Unsigned mode received an unexpectedly signed or invalid executable.' }
        return
    }
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate) {
        throw 'Required Authenticode signature or timestamp is missing/invalid.'
    }
    $tools = @(Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending)
    if ($tools.Count -eq 0) { throw 'SignTool is required to verify signed releases.' }
    & $tools[0].FullName verify /pa /all /v /tw $Executable
    if ($LASTEXITCODE -ne 0) { throw 'SignTool signature/timestamp verification failed.' }
}

function New-ReleaseZip([string]$Directory, [string]$Destination, [DateTimeOffset]$Timestamp) {
    if (Test-Path -LiteralPath $Destination) { throw "Archive already exists: $Destination" }
    if ($Timestamp.Year -lt 1980 -or $Timestamp.Year -gt 2107) { throw 'Source timestamp cannot be represented in ZIP.' }
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $root = [IO.Path]::GetFullPath($Directory)
        foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName) {
            $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
            $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $Timestamp
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
}

function Assert-ReleaseArchive([string]$Archive, $Version, [string]$Commit, [string]$Runtime) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $names = @($zip.Entries | ForEach-Object FullName)
        foreach ($required in @('WindowGather.WinUI.exe', 'README.md', 'VERSION', 'release-manifest.json', 'SHA256SUMS.txt', 'source.zip')) {
            if (@($names | Where-Object { $_ -ceq $required }).Count -ne 1) { throw "Missing/duplicate archive entry: $required" }
        }
        if ($names.Count -ne 6) { throw 'Unexpected release archive contents.' }
        function Read-Entry([string]$Name) {
            $reader = [IO.StreamReader]::new($zip.GetEntry($Name).Open())
            try { $reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        $manifest = (Read-Entry 'release-manifest.json') | ConvertFrom-Json
        if ($manifest.tag -cne $Version.Tag -or $manifest.version -cne $Version.Version -or
            $manifest.assemblyVersion -cne $Version.AssemblyVersion -or $manifest.commit -cne $Commit -or
            $manifest.runtimeIdentifier -cne $Runtime -or $manifest.component -cne 'Window Gather' -or
            (Read-Entry 'VERSION').Trim() -cne $Version.Version) { throw 'Archive source/version identity mismatch.' }
        if ($manifest.authenticodeSigned -isnot [bool] -or
            $manifest.signingProvider -cne $(if ($manifest.authenticodeSigned) { 'Azure Artifact Signing' } else { 'none' })) {
            throw 'Invalid archive signing declaration.'
        }
        $hashes = @{}
        foreach ($name in @('WindowGather.WinUI.exe', 'source.zip')) {
            $stream = $zip.GetEntry($name).Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hashes[$name] = [Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() }
            finally { $sha.Dispose(); $stream.Dispose() }
        }
        if ($manifest.executableSha256 -cne $hashes['WindowGather.WinUI.exe'] -or
            $manifest.sourceArchiveSha256 -cne $hashes['source.zip']) { throw 'Archive bytes differ from the manifest.' }
        $proofDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Archive))
        $proof = Join-Path $proofDirectory ('source-proof-' + [guid]::NewGuid().ToString('N') + '.zip')
        try {
            Invoke-ReleaseGit -C (Split-Path $PSScriptRoot -Parent) archive --format=zip "--output=$proof" $Commit
            if ((Get-ReleaseHash $proof) -cne $hashes['source.zip']) { throw 'Source archive differs from the committed tree.' }
        }
        finally { if (Test-Path -LiteralPath $proof) { Remove-Item -LiteralPath $proof } }
        $expected = "$($hashes['WindowGather.WinUI.exe'])  WindowGather.WinUI.exe`n$($hashes['source.zip'])  source.zip"
        if ((Read-Entry 'SHA256SUMS.txt').Trim().Replace("`r", '') -cne $expected) { throw 'Archive checksum manifest mismatch.' }
        $source = $zip.GetEntry('source.zip').Open()
        $copy = [IO.MemoryStream]::new()
        try { $source.CopyTo($copy) } finally { $source.Dispose() }
        try {
            $bytes = $copy.ToArray()
            $commentLength = [BitConverter]::ToUInt16($bytes, $bytes.Length - 42)
            if ($commentLength -ne 40 -or [Text.Encoding]::ASCII.GetString($bytes, $bytes.Length - 40, 40) -cne $Commit) {
                throw 'Source archive Git commit comment mismatch.'
            }
        }
        finally { $copy.Dispose() }
        return $manifest
    }
    finally { $zip.Dispose() }
}
