[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$releasePath = Join-Path $repoRoot 'Release'
$projectPath = Join-Path $repoRoot 'src/IdleGuard/IdleGuard.csproj'
$executablePath = Join-Path $releasePath 'IdleGuard.exe'

if (Get-Process -Name IdleGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executablePath }) {
    throw 'Exit IdleGuard from its tray menu before updating the release.'
}

& dotnet publish $projectPath -p:PublishProfile=Compact -p:Platform=x64 -c Release -o $releasePath
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }

$docsPath = Join-Path $releasePath 'docs'
New-Item -ItemType Directory -Path $docsPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/release-readme.md') -Destination (Join-Path $docsPath 'README.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/release-readme.zh-CN.md') -Destination (Join-Path $docsPath 'README.zh-CN.md')

$bytes = (Get-ChildItem -LiteralPath $releasePath -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ('Ready: {0} ({1:N1} MiB)' -f $executablePath, ($bytes / 1MB))
