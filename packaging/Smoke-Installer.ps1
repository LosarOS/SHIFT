[CmdletBinding()]
param([ValidatePattern('^\d+\.\d+\.\d+(?:-rc\.\d+)?$')][string]$Version='1.0.0-rc.1')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Installer smoke requires a disposable GitHub Actions Windows runner.'}
$repo=Split-Path $PSScriptRoot -Parent
$out=Join-Path $repo 'artifacts'
$install=Join-Path $out 'installed-smoke'
$setup=Join-Path $out "SHIFT-$Version-win-x64-setup.exe"
$uninstallKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E4FAF163-A081-42F6-9974-0729E1397AA3}_is1'
if(Test-Path $uninstallKey){throw 'Refusing to replace an existing SHIFT installation.'}
$process=Start-Process $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',"/DIR=`"$install`"") -Wait -PassThru
if($process.ExitCode -ne 0){throw "Installer exited $($process.ExitCode)"}
try {
    if(-not(Test-Path (Join-Path $install 'SHIFT.exe'))){throw 'Installed executable missing.'}
    & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Windows.ps1') -AppDirectory $install -Installed -DiagnosticsDirectory (Join-Path $out 'diagnostics')
    if($LASTEXITCODE -ne 0){throw 'Installed application UI smoke failed.'}
} finally {
    $uninstaller=Join-Path $install 'unins000.exe'
    if(Test-Path $uninstaller){$process=Start-Process $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru;if($process.ExitCode -ne 0){throw 'Uninstall failed.'}}
}
if(Test-Path $uninstallKey){throw 'Uninstall registry key remains.'}
if(Test-Path (Join-Path $install 'SHIFT.exe')){throw 'Executable remains after uninstall.'}
Write-Host 'Installer smoke passed: per-user install, installed UI flow, uninstall, registry cleanup.'
