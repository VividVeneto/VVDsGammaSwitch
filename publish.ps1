param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    dotnet publish src/GammaSwitch/GammaSwitch.csproj -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist/GammaSwitch
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $PSScriptRoot 'dist\GammaSwitch\README.md') -Force
    Get-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\GammaSwitch\GammaSwitch.exe') | Select-Object FullName,Length
}
finally { Pop-Location }
