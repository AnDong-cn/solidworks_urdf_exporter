# Run the copy of this script inside a backup directory from an elevated PowerShell.
$ErrorActionPreference = 'Stop'
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Close SolidWorks before restoring.' }
$manifest = Get-Content "$PSScriptRoot\manifest.json" -Raw | ConvertFrom-Json
$target = [IO.Path]::GetFullPath($manifest.InstallPath)
if ($target -ne 'C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter') { throw "Unexpected restore destination: $target" }
foreach ($file in $manifest.Files) {
    if ((Get-FileHash -LiteralPath "$PSScriptRoot\plugin\$($file.Path)" -Algorithm SHA256).Hash -ne $file.SHA256) {
        throw "Backup file changed: $($file.Path)"
    }
}
$regasm = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
& $regasm "$target\SW2URDF.dll" /unregister
if ($LASTEXITCODE) { throw 'Could not unregister current exporter.' }
Get-ChildItem -LiteralPath "$PSScriptRoot\plugin" -Force | Copy-Item -Destination $target -Recurse -Force
& $regasm "$target\SW2URDF.dll" /codebase
if ($LASTEXITCODE) { throw 'Could not register restored exporter.' }
foreach ($export in $manifest.RegistryExports) {
    & reg.exe import "$PSScriptRoot\$export" /reg:64
    if ($LASTEXITCODE) { throw "Registry restore failed: $export" }
}
foreach ($missing in $manifest.MissingRegistryKeys) {
    if ($missing -notmatch '65c9fc17-6a74-45a3-8f84-55185900275d|SW2URDF\.SW\.SwAddin|E43E85A9-071D-430A-91B2-84B7AB923170') {
        throw "Unexpected registry key: $missing"
    }
    & reg.exe query $missing /reg:64 *> $null
    if ($LASTEXITCODE -eq 0) {
        & reg.exe delete $missing /f /reg:64
        if ($LASTEXITCODE) { throw "Could not restore the original absence of $missing" }
    }
}
Write-Output 'Previous exporter files and registration restored.'
