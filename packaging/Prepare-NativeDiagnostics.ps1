# Disposable CI-only tools. Never included in publish/installer/portable output.
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Native crash capture is restricted to disposable GitHub Actions runners.'}
$root=Join-Path $env:RUNNER_TEMP 'SHIFT-native-diagnostics'
New-Item $root -ItemType Directory -Force | Out-Null
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.debugging.platform.dbgeng/20260319.1511.0/microsoft.debugging.platform.dbgeng.20260319.1511.0.nupkg' -OutFile "$root/dbgeng.zip"
Expand-Archive "$root/dbgeng.zip" "$root/dbgeng" -Force
Invoke-WebRequest 'https://download.sysinternals.com/files/Procdump.zip' -OutFile "$root/procdump.zip"
Expand-Archive "$root/procdump.zip" "$root/procdump" -Force
$monitor=Join-Path $root 'procdump/procdump64.exe'
$signature=Get-AuthenticodeSignature $monitor
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'Expected a signed Microsoft ProcDump executable.'}
$vswhere="${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs= & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$probe=Join-Path $root 'dbgeng/content/amd64/NativeCrashProbe.exe'
$source=Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/NativeCrashProbe.cpp'
$command="call `"$vcvars`" >nul && cl /nologo /EHsc /std:c++17 `"$source`" /I`"$root/dbgeng/build/native/inc`" /Fo`"$root/probe.obj`" /link /LIBPATH:`"$root/dbgeng/build/native/lib/amd64`" dbgeng.lib ole32.lib /OUT:`"$probe`""
& cmd.exe /d /s /c $command
if($LASTEXITCODE -ne 0){throw 'Native dump diagnostic helper compilation failed.'}
"SHIFT_CRASH_MONITOR=$monitor" >> $env:GITHUB_ENV
"SHIFT_CRASH_PROBE=$probe" >> $env:GITHUB_ENV
