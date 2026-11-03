param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test,
    [switch]$Installer
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Push-Location $root
try {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = & $vswhere -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { throw 'Install Visual Studio 2022 with the components in .vsconfig.' }
    & $msbuild SW2URDF/SW2URDF.csproj /t:Restore /p:RestorePackagesConfig=true /nologo /v:minimal
    if ($LASTEXITCODE) { throw 'NuGet restore failed.' }
    & $msbuild SW2URDF/SW2URDF.csproj /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 /p:UseSharedCompilation=false /nologo /v:minimal
    if ($LASTEXITCODE) { throw 'Exporter build failed.' }
    if ($Test) {
        $dotnet = if (Test-Path artifacts/tools/dotnet/dotnet.exe) { (Resolve-Path artifacts/tools/dotnet/dotnet.exe).Path } else { 'dotnet' }
        & $dotnet build TestRunner/TestRunner.csproj --configuration $Configuration --nologo -p:NuGetAudit=false
        if ($LASTEXITCODE) { throw 'Test runner build failed.' }
        & "TestRunner/bin/$Configuration/net48/TestRunner.exe" TestSTEPExport "SW2URDF/bin/x64/$Configuration/SW2URDF.dll"
        if ($LASTEXITCODE) { throw 'STEP integration tests failed.' }
    }
    if ($Installer) {
        if ($Configuration -ne 'Release') { throw 'Build installers from Release only.' }
        $iscc = @('artifacts/tools/inno/ISCC.exe', "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
        if (!$iscc) { throw 'Install Inno Setup 6, or place ISCC.exe in artifacts/tools/inno.' }
        & $iscc INSTALL/Install.iss
        if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    }
} finally { Pop-Location }
