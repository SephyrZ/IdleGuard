[CmdletBinding()]
param(
    [string]$OutputDirectory = 'Release',
    [switch]$Untrimmed,
    [switch]$KeepBuildArtifacts
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$releasePath = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$projectPath = Join-Path $repoRoot 'src/IdleGuard/IdleGuard.csproj'
$executablePath = Join-Path $releasePath 'IdleGuard.exe'
# Use a new directory on every invocation: Rebuild alone does not remove DLLs
# left behind by packages that have since been removed from the project.
$runPath = Join-Path $repoRoot ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
$buildPath = (Join-Path $runPath 'build') + [IO.Path]::DirectorySeparatorChar
$intermediatePath = (Join-Path $runPath 'obj') + [IO.Path]::DirectorySeparatorChar
$stagingPath = Join-Path $runPath 'publish'
$auditPath = Join-Path $runPath 'dependencies.txt'
$trimmed = if ($Untrimmed) { 'false' } else { 'true' }
New-Item -ItemType Directory -Path $runPath -Force | Out-Null

if (Get-Process -Name IdleGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }) {
    throw 'Exit IdleGuard from its tray menu before updating the release.'
}

# A normal development build and a single-file build have different WinUI
# initialization/resource requirements. Never reuse incremental development
# output for publishing; build the release profile fully before bundling it.
& dotnet build $projectPath -t:Rebuild -p:PublishProfile=Compact -p:Platform=x64 "-p:OutDir=$buildPath" "-p:IntermediateOutputPath=$intermediatePath" "-p:PublishTrimmed=$trimmed" -c Release | Tee-Object -FilePath (Join-Path $runPath 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Release rebuild failed.' }
& dotnet publish $projectPath -p:PublishProfile=Compact -p:Platform=x64 "-p:OutDir=$buildPath" "-p:IntermediateOutputPath=$intermediatePath" "-p:PublishTrimmed=$trimmed" "-p:PublishAuditPath=$auditPath" -c Release --no-build --no-restore -o $stagingPath | Tee-Object -FilePath (Join-Path $runPath 'publish.log')
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }

# Keep the dependency inventory alongside build artifacts, outside the release.
$inventory = foreach ($line in Get-Content -LiteralPath $auditPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $sourcePath, $relativePath = $line -split '\|', 2
    $file = Get-Item -LiteralPath $sourcePath
    [pscustomobject]@{ RelativePath = $relativePath; Bytes = $file.Length; Source = $sourcePath }
}
if (@($inventory).Count -eq 0) { throw 'The single-file dependency inventory is empty.' }
$inventory | Sort-Object Bytes -Descending | Export-Csv -LiteralPath (Join-Path $runPath 'dependencies.csv') -NoTypeInformation -Encoding utf8
if (Get-Process -Name IdleGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }) {
    throw "Exit IdleGuard before updating the release. The completed build is preserved at $stagingPath"
}
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $stagingPath 'IdleGuard.exe') -Destination $executablePath

$docsPath = Join-Path $releasePath 'docs'
New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/release-readme.md') -Destination (Join-Path $docsPath 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/release-readme.zh-CN.md') -Destination (Join-Path $docsPath 'README.zh-CN.md')

$bytes = (Get-ChildItem -LiteralPath $releasePath -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ('Ready: {0} ({1:N1} MiB)' -f $executablePath, ($bytes / 1MB))
Write-Host ('Dependency inventory: {0}' -f (Join-Path $runPath 'dependencies.csv'))

if (!$KeepBuildArtifacts) {
    $reportPath = Join-Path $repoRoot 'artifacts/latest-publish'
    New-Item -ItemType Directory -Path $reportPath -Force | Out-Null
    foreach ($report in @('build.log', 'publish.log', 'dependencies.txt', 'dependencies.csv')) {
        Copy-Item -LiteralPath (Join-Path $runPath $report) -Destination (Join-Path $reportPath $report)
    }
    [pscustomobject]@{
        Executable = $executablePath
        Bytes = (Get-Item -LiteralPath $executablePath).Length
        Sha256 = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        Trimmed = !$Untrimmed
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportPath 'release.json') -Encoding utf8

    # Only remove this invocation's generated directory, never the destination.
    $resolvedArtifacts = (Resolve-Path -LiteralPath (Join-Path $repoRoot 'artifacts')).Path
    $resolvedRun = (Resolve-Path -LiteralPath $runPath).Path
    if ((Split-Path -Parent $resolvedRun) -ne $resolvedArtifacts -or
        (Split-Path -Leaf $resolvedRun) -notmatch '^publish-[0-9a-f]{32}$' -or
        $releasePath.StartsWith($resolvedRun + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean an unexpected build directory.'
    }
    Remove-Item -LiteralPath $resolvedRun -Recurse -Force
    Write-Host ('Build intermediates removed; reports: {0}' -f $reportPath)
}
