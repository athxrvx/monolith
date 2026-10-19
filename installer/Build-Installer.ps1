param(
    [string]$AppPublishDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$appPublish = [IO.Path]::GetFullPath($AppPublishDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $PSScriptRoot 'obj/InstallerStaging'
$payload = Join-Path $staging 'Monolith-Payload.zip'

if (-not (Test-Path (Join-Path $appPublish 'Monolith.exe'))) {
    throw "Run the WinUI app publish step first; '$appPublish/Monolith.exe' is missing."
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null
cargo build --release --manifest-path (Join-Path $root 'engine/Cargo.toml') --bin monolith_uninstall
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }

$payloadRoot = Join-Path $staging 'payload'
if (Test-Path $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null
Get-ChildItem -LiteralPath $appPublish -Force |
    Where-Object { $_.Name -notin @('Monolith-Setup.exe', 'Monolith-Setup.pdb', 'Monolith-Setup.deps.json', 'Monolith-Setup.runtimeconfig.json') } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $payloadRoot -Recurse -Force }
Copy-Item (Join-Path $root 'target/release/monolith_uninstall.exe') (Join-Path $payloadRoot 'MonolithUninstall.exe') -Force
if (Test-Path $payload) { Remove-Item -LiteralPath $payload -Force }
Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payload -CompressionLevel Optimal

$installerOutput = Join-Path $staging 'installer-output'
dotnet publish (Join-Path $PSScriptRoot 'MonolithSetup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $installerOutput
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Copy-Item -LiteralPath (Join-Path $installerOutput 'Monolith-Setup.exe') -Destination (Join-Path $output 'Monolith-Setup.exe') -Force
Remove-Item -LiteralPath $staging -Recurse -Force
Write-Host "Created $(Join-Path $output 'Monolith-Setup.exe')"
