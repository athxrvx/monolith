param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$publish = [IO.Path]::GetFullPath((Join-Path $root 'publish'))
if ((Split-Path -Leaf $publish) -ne 'publish' -or
    -not [string]::Equals((Split-Path -Parent $publish), $root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unexpected publish path: $publish"
}

Write-Host 'Building the Monolith window-management engine...'
cargo build --release --manifest-path (Join-Path $root 'engine\Cargo.toml') --package monolith_engine --bin monolith_engine
if ($LASTEXITCODE -ne 0) { throw 'Monolith engine build failed.' }

$embeddedEngine = Join-Path $root 'studio\Assets\monolith_engine.exe'
Copy-Item -LiteralPath (Join-Path $root 'target\release\monolith_engine.exe') -Destination $embeddedEngine -Force

if (Test-Path -LiteralPath $publish) {
    $publishItem = Get-Item -LiteralPath $publish -Force
    if (($publishItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to clear a publish path that is a reparse point: $publish"
    }
    Remove-Item -LiteralPath $publish -Recurse -Force
}
New-Item -ItemType Directory -Path $publish | Out-Null

Write-Host 'Publishing the WinUI app...'
dotnet publish (Join-Path $root 'studio\Monolith.Studio.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Monolith app publish failed.' }

Write-Host 'Building the per-user installer...'
& (Join-Path $root 'installer\Build-Installer.ps1') -AppPublishDirectory $publish -OutputDirectory $publish
if ($LASTEXITCODE -ne 0) { throw 'Monolith installer build failed.' }

foreach ($requiredFile in @('Monolith.exe', 'Monolith-Setup.exe')) {
    $path = Join-Path $publish $requiredFile
    if (-not (Test-Path -LiteralPath $path)) { throw "Expected release artifact was not created: $path" }
}

Write-Host "Release ready in $publish"
