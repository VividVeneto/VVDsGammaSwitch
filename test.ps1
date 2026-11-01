param([switch]$Native,[switch]$NativeWrites)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/GammaSwitch.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed: $LASTEXITCODE" }
    if ($Native -or $NativeWrites) {
        $nativeArguments = @('run','--project','tests/GammaSwitch.NativeTests','-c','Release')
        if ($NativeWrites) { $nativeArguments += @('--','--same-state') }
        & dotnet @nativeArguments
        if ($LASTEXITCODE -ne 0) { throw "Native tests failed: $LASTEXITCODE" }
    }
}
finally { Pop-Location }
