# Actual RC7 -> RC8 portable upgrade and downgrade refusal. Owned disposable Windows CI only.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppDirectory,[Parameter(Mandatory=$true)][string]$DiagnosticsDirectory)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP){throw 'Disposable Windows CI only.'}
$app=(Resolve-Path -LiteralPath $AppDirectory).Path
if(-not(Test-Path -LiteralPath (Join-Path $app 'portable.flag')) -or (Test-Path -LiteralPath (Join-Path $app 'data'))){throw 'Clean extracted candidate portable required.'}
$owner=[guid]::NewGuid()
$root=Join-Path $env:RUNNER_TEMP ('shift-rc7-upgrade-'+$owner.ToString('N'))
if(Test-Path -LiteralPath $root){throw 'Owned temporary directory already exists.'}
New-Item $root -ItemType Directory | Out-Null
$owner.ToString() | Set-Content (Join-Path $root '.upgrade-owner')
New-Item $DiagnosticsDirectory -ItemType Directory -Force | Out-Null
$diagnostics=(Resolve-Path -LiteralPath $DiagnosticsDirectory).Path
$repo=Split-Path $PSScriptRoot -Parent
$tool=Join-Path $repo 'tools/Shift.StudioAcceptance/bin/Release/net10.0/Shift.StudioAcceptance.dll'
$source='7bb1760b7095bcc254b25807b9eb2934cd807919'
$tag='v1.0.0-rc.7'
$process=$null
function Tool([string]$Stage,[string]$Data,[string]$Extra=''){
 $arguments=@($tool,$Stage,$Data,$owner.ToString());if($Extra){$arguments+=$Extra}
 & dotnet @arguments
 if($LASTEXITCODE -ne 0){throw "RC7 upgrade acceptance failed: $Stage"}
}
try {
 & dotnet restore (Join-Path $repo 'tools/Shift.StudioAcceptance') --locked-mode --nologo
 if($LASTEXITCODE -ne 0){throw 'Acceptance tool restore failed.'}
 & dotnet build (Join-Path $repo 'tools/Shift.StudioAcceptance') -c Release --no-restore --nologo
 if($LASTEXITCODE -ne 0){throw 'Acceptance tool build failed.'}
 $base='https://github.com/LosarOS/SHIFT/releases/download/'+$tag
 $zip=Join-Path $root 'rc7-portable.zip'
 $sums=Join-Path $root 'SHA256SUMS.txt'
 $manifest=Join-Path $root 'WINDOWS-VALIDATION.json'
 Invoke-WebRequest ($base+'/SHIFT-1.0.0-rc.7-win-x64-portable.zip') -OutFile $zip -UseBasicParsing
 Invoke-WebRequest ($base+'/SHA256SUMS.txt') -OutFile $sums -UseBasicParsing
 Invoke-WebRequest ($base+'/WINDOWS-VALIDATION.json') -OutFile $manifest -UseBasicParsing
 $expected='4ee847d358c89eab5b3add2228db9c4d5f51762595ddd60776086595d2d8d98d'
 $actual=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
 if($actual -cne $expected -or -not((Get-Content -LiteralPath $sums) -ccontains ($expected+'  SHIFT-1.0.0-rc.7-win-x64-portable.zip'))){throw 'Immutable RC7 portable hash mismatch.'}
 $oldManifest=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
 if($oldManifest.source_commit -cne $source -or $oldManifest.version -cne '1.0.0-rc.7'){throw 'RC7 source binding mismatch.'}
 $oldSmoke=Join-Path $root 'Smoke-RC7.ps1'
 Invoke-WebRequest ('https://raw.githubusercontent.com/LosarOS/SHIFT/'+$source+'/packaging/Smoke-Windows.ps1') -OutFile $oldSmoke -UseBasicParsing
 if((Get-FileHash -LiteralPath $oldSmoke -Algorithm SHA256).Hash.ToLowerInvariant() -cne '3ff83fd85acea23d0182029479661d6d1b2237ccb87f08f9e0238de384094a47'){throw 'Pinned RC7 smoke script hash mismatch.'}
 Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'old')
 $oldApp=Join-Path $root 'old/SHIFT-1.0.0-rc.7-win-x64-portable'
 & powershell.exe -NoProfile -File $oldSmoke -AppDirectory $oldApp -DataOwner $owner -PreserveData -ExtractedArchive -DiagnosticsDirectory (Join-Path $diagnostics 'actual-rc7-baseline')
 if($LASTEXITCODE -ne 0){throw 'Actual immutable RC7 UI baseline failed.'}
 $oldData=Join-Path $oldApp 'data'
 $candidate=Join-Path $root 'candidate'
 New-Item $candidate -ItemType Directory | Out-Null
 Get-ChildItem -LiteralPath $app -Force | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $candidate -Recurse}
 $data=Join-Path $candidate 'data';New-Item $data -ItemType Directory | Out-Null
 $owner.ToString() | Set-Content (Join-Path $data '.studio-ci-owner')
 $owner.ToString() | Set-Content (Join-Path $data '.shift-ci-owner')
 Tool 'upgrade-rc7-seed' $data (Join-Path $oldData 'shift.db')
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Windows.ps1') -AppDirectory $candidate -DataOwner $owner -ResumeOwnedData -PreserveData -ExtractedArchive -DiagnosticsDirectory (Join-Path $diagnostics 'upgraded-rc8')
 if($LASTEXITCODE -ne 0){throw 'RC8 could not open/restart actual RC7 data.'}
 Tool 'upgrade-rc7-check' $data
 $upgradedBefore=(Get-FileHash -LiteralPath (Join-Path $data 'shift.db') -Algorithm SHA256).Hash
 # Preserve the original v2 fixture; the old executable receives only a separate owned copy of v3.
 Move-Item -LiteralPath $oldData -Destination (Join-Path $oldApp 'actual-v2-preserved')
 New-Item $oldData -ItemType Directory | Out-Null
 $owner.ToString() | Set-Content (Join-Path $oldData '.studio-ci-owner')
 Tool 'upgrade-rc7-downgrade-snapshot' $oldData (Join-Path $data 'shift.db')
 $downgrade=Join-Path $oldData 'shift.db'
 $before=(Get-FileHash -LiteralPath $downgrade -Algorithm SHA256).Hash
 Add-Type -AssemblyName UIAutomationClient
 Add-Type -AssemblyName UIAutomationTypes
 $process=Start-Process (Join-Path $oldApp 'SHIFT.exe') -WorkingDirectory $oldApp -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(40);$refused=$false
 while([DateTime]::UtcNow -lt $deadline){
  $process.Refresh();if($process.HasExited){throw 'Old RC7 exited without visible downgrade explanation.'}
  if($process.MainWindowHandle -ne [IntPtr]::Zero){
   try {
    $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)
    $labels=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    $names=@($labels | ForEach-Object {$_.Current.Name})
    if(@($names | Where-Object {$_ -like '*This database belongs to a newer SHIFT version*'}).Count -gt 0 -and @($names | Where-Object {$_ -eq 'SHIFT не смог открыть локальные данные' -or $_ -eq 'SHIFT could not open local data'}).Count -gt 0){$refused=$true;break}
   } catch [System.Windows.Automation.ElementNotAvailableException] { }
  }
  Start-Sleep -Milliseconds 200
 }
 if(-not $refused){throw 'Old RC7 did not visibly refuse schema3.'}
 if(-not $process.CloseMainWindow() -or -not $process.WaitForExit(15000) -or $process.ExitCode -ne 0){throw 'Old RC7 refusal did not exit gracefully.'}
 $process.Dispose();$process=$null
 if((Get-FileHash -LiteralPath $downgrade -Algorithm SHA256).Hash -cne $before){throw 'Old RC7 modified the downgraded v3 copy.'}
 Tool 'upgrade-rc7-check' $data
 if((Get-FileHash -LiteralPath (Join-Path $data 'shift.db') -Algorithm SHA256).Hash -cne $upgradedBefore){throw 'Original upgraded database changed during downgrade-copy acceptance.'}
 @{status='passed';source_commit=$env:GITHUB_SHA;baseline_source=$source;baseline_version='1.0.0-rc.7';baseline_portable_sha256=$actual;actual_rc7_ui_baseline=$true;actual_schema2_to3_upgrade=$true;workspace_history_settings_preserved=$true;validated_schema2_safety_backup=$true;old_rc7_visible_downgrade_refusal=$true;downgrade_copy_sha256_unchanged=$true;original_upgraded_profile_unchanged=$true;manual_acceptance=$false;environment='disposable Windows runner; owned portable data only'} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $diagnostics 'rc7-upgrade-smoke.json')
 Write-Host 'Actual RC7 upgrade, preservation and visible downgrade refusal passed.'
} finally {
 if($null -ne $process){$process.Refresh();if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 if((Test-Path -LiteralPath (Join-Path $root '.upgrade-owner')) -and (Get-Content -LiteralPath (Join-Path $root '.upgrade-owner') -Raw).Trim() -ceq $owner.ToString()){Remove-Item -LiteralPath $root -Recurse -Force}
}
