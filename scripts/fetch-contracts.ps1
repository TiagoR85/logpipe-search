param([string]$Ref = 'v1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'packages'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$project = 'src/LogPipe.Contracts/LogPipe.Contracts.csproj'
$packed = $false
try {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("logpipe-contracts-" + [guid]::NewGuid().ToString('N'))
    git clone --depth 1 --branch $Ref https://github.com/TiagoR85/logpipe-contracts.git $tmp 2>$null
    if ($LASTEXITCODE -ne 0) { throw "git clone of tag $Ref failed" }
    dotnet pack (Join-Path $tmp $project) -c Release -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }
    Remove-Item -Recurse -Force $tmp
    $packed = $true
} catch {
    Write-Warning "Tag clone/pack failed: $($_.Exception.Message). Trying sibling checkout."
}
if (-not $packed) {
    $sibling = Join-Path (Split-Path -Parent $root) 'logpipe-contracts'
    if (-not (Test-Path $sibling)) { throw "LogPipe.Contracts unavailable: clone failed and no sibling checkout at $sibling" }
    dotnet pack (Join-Path $sibling $project) -c Release -o $out
    if ($LASTEXITCODE -ne 0) { throw "sibling pack failed" }
}
Write-Output "LogPipe.Contracts package ready in $out"
