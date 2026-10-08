$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$allowed = @{
    'WindowGather.Domain' = @()
    'WindowGather.Application' = @('WindowGather.Domain')
    'WindowGather.Infrastructure.Windows' = @('WindowGather.Application')
    'WindowGather.Presentation' = @('WindowGather.Application')
    'WindowGather.WinUI' = @('WindowGather.Presentation', 'WindowGather.Infrastructure.Windows')
}
foreach ($name in $allowed.Keys) {
    $path = Join-Path $root "$name\$name.csproj"
    [xml]$project = Get-Content -LiteralPath $path -Raw
    $references = @($project.SelectNodes('//ProjectReference') | ForEach-Object {
        [IO.Path]::GetFileNameWithoutExtension($_.Include)
    })
    $difference = @(Compare-Object -ReferenceObject @($allowed[$name]) -DifferenceObject $references)
    if ($difference.Count -ne 0) { throw "Dependency contract changed for $name." }
    if ($name -in @('WindowGather.Domain', 'WindowGather.Application', 'WindowGather.Presentation')) {
        if ($project.Project.PropertyGroup.TargetFramework -ne 'net10.0') { throw "$name must target portable net10.0." }
        if ($project.SelectNodes('//UseWindowsForms | //UseWinUI').Count -ne 0) { throw "$name cannot enable a native UI framework." }
    }
    if ($name -in @('WindowGather.Domain', 'WindowGather.Application') -and $project.SelectNodes('//PackageReference').Count -ne 0) {
        throw "$name must remain free of third-party packages."
    }
}
Write-Output 'PASS project dependency boundaries.'
