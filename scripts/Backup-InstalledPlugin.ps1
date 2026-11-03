param([string]$InstallPath = 'C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$destination = Join-Path $root ('artifacts\backup\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (!(Test-Path (Join-Path $InstallPath 'SW2URDF.dll'))) { throw 'Installed plugin DLL was not found.' }
New-Item -ItemType Directory -Path "$destination\plugin" -Force | Out-Null
Get-ChildItem -LiteralPath $InstallPath -Force | Copy-Item -Destination "$destination\plugin" -Recurse -Force
$keys = @(
    'HKLM\SOFTWARE\Classes\CLSID\{65c9fc17-6a74-45a3-8f84-55185900275d}',
    'HKLM\SOFTWARE\Classes\SW2URDF.SW.SwAddin',
    'HKLM\SOFTWARE\SolidWorks\Addins\{65c9fc17-6a74-45a3-8f84-55185900275d}',
    'HKCU\Software\SolidWorks\AddInsStartup\{65c9fc17-6a74-45a3-8f84-55185900275d}',
    'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E43E85A9-071D-430A-91B2-84B7AB923170}_is1'
)
$exports = @()
$missingKeys = @()
for ($index = 0; $index -lt $keys.Count; $index++) {
    & reg.exe query $keys[$index] /reg:64 *> $null
    if ($LASTEXITCODE -eq 0) {
        $filename = "registry-$index.reg"
        & reg.exe export $keys[$index] "$destination\$filename" /y /reg:64 | Out-Null
        if ($LASTEXITCODE) { throw "Could not back up $($keys[$index])." }
        $exports += $filename
    } else {
        $missingKeys += $keys[$index]
    }
}
$hashes = @(Get-ChildItem -LiteralPath $InstallPath -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($InstallPath.TrimEnd('\').Length + 1)
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    if ($hash -ne (Get-FileHash -LiteralPath "$destination\plugin\$relative" -Algorithm SHA256).Hash) { throw "Backup mismatch: $relative" }
    @{ Path = $relative; SHA256 = $hash }
})
@{ InstallPath = $InstallPath; RegistryExports = $exports; MissingRegistryKeys = $missingKeys; Files = $hashes } |
    ConvertTo-Json -Depth 5 | Set-Content "$destination\manifest.json" -Encoding UTF8
Copy-Item "$PSScriptRoot\Restore-InstalledPlugin.ps1" "$destination\Restore-InstalledPlugin.ps1"
Write-Output $destination
