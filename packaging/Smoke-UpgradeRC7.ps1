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

 # Retain the immutable original. Only its UIA Set-Field and Quick-Key readiness helpers are adapted:
 # Server runner 43653711472 exposed a transient Edit peer without ValuePattern.
 # No feature assertion/action is removed; each SetValue and each SendWait remains single-shot.
 $originalHarnessHash=(Get-FileHash -LiteralPath $oldSmoke -Algorithm SHA256).Hash.ToLowerInvariant()
 $originalHarness=[IO.File]::ReadAllText($oldSmoke,[Text.Encoding]::UTF8).Replace("`r`n","`n")
 $oldSetField=@'
function Set-Field([string]$Id,[string]$Value) {
    $control = Wait-For { Find-Control '' ([System.Windows.Automation.ControlType]::Edit) $Id } "field $Id"
    $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
'@
 $readySetField=@'
function Set-Field([string]$Id,[string]$Value) {
    Write-Host "RC7 compatibility harness: waiting for writable ValuePattern on $Id"
    $pattern = Wait-For {
        $candidate=Find-Control '' ([System.Windows.Automation.ControlType]::Edit) $Id
        if($null -eq $candidate -or -not $candidate.Current.IsEnabled){return $null}
        [object]$current=$null
        try {
            if($candidate.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$current) -and
               $null -ne $current -and -not $current.Current.IsReadOnly){return $current}
        } catch [System.Windows.Automation.ElementNotAvailableException] { return $null }
        return $null
    } "writable field pattern $Id"
    $ambiguousSetValue=$false
    try { $pattern.SetValue($Value) }
    catch [System.Windows.Automation.ElementNotAvailableException] {
        $ambiguousSetValue=$true
        Write-Host "RC7 compatibility harness: SetValue peer replaced for $Id; checking postcondition without another write."
    }
    $expectedValue=$Value.Replace("`r`n","`n").Replace("`r","`n")
    $null=Wait-For {
        $candidate=Find-Control '' ([System.Windows.Automation.ControlType]::Edit) $Id
        if($null -eq $candidate){return $null}
        [object]$current=$null
        try {
            if($candidate.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$current) -and
               $null -ne $current -and $current.Current.Value.Replace("`r`n","`n").Replace("`r","`n") -ceq $expectedValue){return $true}
        } catch [System.Windows.Automation.ElementNotAvailableException] { return $null }
        return $null
    } "field value acknowledged $Id"
    if($ambiguousSetValue){Write-Host "RC7 compatibility harness: exact fresh-peer value acknowledged for $Id after ambiguous SetValue; no write was retried."}
}
'@
 $oldQuickKey=@'
function Quick-Key([string]$Keys) {
    # Never inject into another program or an unverified modal dialog. Each key is sent once;
    # retries only reacquire the intended foreground window/focus, never repeat dispatch.
    $field = Wait-For { Find-Control '' ([System.Windows.Automation.ControlType]::Edit) 'Quick.Search' } 'compact keyboard target'
    [void][ShiftWindowProbe]::SetForegroundWindow($process.MainWindowHandle)
    $field.SetFocus()
    $null = Wait-For {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ([ShiftWindowProbe]::GetForegroundWindow() -eq $process.MainWindowHandle -and $focused -and
            $focused.Current.ProcessId -eq $process.Id -and $focused.Current.AutomationId -ceq 'Quick.Search') { $true }
    } 'owned compact launcher keyboard focus'
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}
'@
 $readyQuickKey=@'
function Quick-Key([string]$Keys) {
    # Never inject into another program or an unverified modal dialog. Each key is sent once;
    # retries only reacquire the intended foreground window/focus, never repeat dispatch.
    $focusState=@{id='';samples=0}
    $null = Wait-For {
        $field=Find-Control '' ([System.Windows.Automation.ControlType]::Edit) 'Quick.Search'
        if($null -eq $field -or -not $field.Current.IsEnabled){return $null}
        [void][ShiftWindowProbe]::SetForegroundWindow($process.MainWindowHandle)
        try { $field.SetFocus() }
        catch [System.Windows.Automation.ElementNotAvailableException] { $focusState.samples=0;return $null }
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        $identity=$field.GetRuntimeId() -join ':'
        if ([ShiftWindowProbe]::GetForegroundWindow() -eq $process.MainWindowHandle -and $focused -and
            $focused.Current.ProcessId -eq $process.Id -and $focused.Current.AutomationId -ceq 'Quick.Search' -and
            ($focused.GetRuntimeId() -join ':') -ceq $identity) {
            if($focusState.id -ceq $identity){$focusState.samples++}else{$focusState.id=$identity;$focusState.samples=1}
            if($focusState.samples -ge 3){return $true}
        }else{$focusState.samples=0}
        return $null
    } 'owned compact launcher keyboard focus'
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}
'@
 if(($originalHarness.Split([string[]]@($oldSetField),[StringSplitOptions]::None)).Length -ne 2){throw 'Pinned RC7 Set-Field helper differs; refusing broad harness replacement.'}
 if(($originalHarness.Split([string[]]@($oldQuickKey),[StringSplitOptions]::None)).Length -ne 2){throw 'Pinned RC7 Quick-Key helper differs; refusing broad harness replacement.'}
 $patchedHarness=$originalHarness.Replace($oldSetField,$readySetField).Replace($oldQuickKey,$readyQuickKey)
 $compatibleSmoke=Join-Path $root 'Smoke-RC7-Compatible.ps1'
 [IO.File]::WriteAllText($compatibleSmoke,$patchedHarness,(New-Object Text.UTF8Encoding($true)))
 $parseTokens=$null;$parseErrors=$null
 $null=[System.Management.Automation.Language.Parser]::ParseFile($compatibleSmoke,[ref]$parseTokens,[ref]$parseErrors)
 if(@($parseErrors).Count -gt 0){throw ('Compatible RC7 harness parse failed: '+(($parseErrors | ForEach-Object {$_.Message}) -join '; '))}
 $patchLines=@('--- Smoke-RC7-original.ps1','+++ Smoke-RC7-compatible.ps1')
 foreach($helper in @(@{before=$oldSetField;after=$readySetField},@{before=$oldQuickKey;after=$readyQuickKey})){
  $oldLines=@($helper.before -split "`n");$newLines=@($helper.after -split "`n")
  $oldStart=@($originalHarness.Substring(0,$originalHarness.IndexOf($helper.before,[StringComparison]::Ordinal)) -split "`n").Count
  $newStart=@($patchedHarness.Substring(0,$patchedHarness.IndexOf($helper.after,[StringComparison]::Ordinal)) -split "`n").Count
  $patchLines+=("@@ -{0},{1} +{2},{3} @@" -f $oldStart,$oldLines.Count,$newStart,$newLines.Count)
  $patchLines+=@($oldLines | ForEach-Object {'-'+$_});$patchLines+=@($newLines | ForEach-Object {'+'+$_})
 }
 [IO.File]::WriteAllText((Join-Path $diagnostics 'rc7-harness.patch'),($patchLines -join "`n")+"`n",(New-Object Text.UTF8Encoding($false)))
 $compatibleHarnessHash=(Get-FileHash -LiteralPath $compatibleSmoke -Algorithm SHA256).Hash.ToLowerInvariant()
 Copy-Item -LiteralPath $oldSmoke -Destination (Join-Path $diagnostics 'Smoke-RC7-original.ps1')
 Copy-Item -LiteralPath $compatibleSmoke -Destination (Join-Path $diagnostics 'Smoke-RC7-compatible.ps1')
 @{original_sha256=$originalHarnessHash;compatible_sha256=$compatibleHarnessHash;patch='Set-Field writable ValuePattern/exact readback (ElementNotAvailable is ambiguous, never retried) + Quick-Key stable owned focus; one SetValue/one SendWait per call';reason='CI43653711472 Set-Field Unsupported Pattern; CI43654962743 Quick-Key SetFocus ElementNotAvailable (identical original RC7 helper); CI43657366311 SetValue ElementNotAvailable requires exact fresh-peer postcondition';diff_file='rc7-harness.patch';compatible_parser_passed=$true;readback_newlines_normalized=$true;original_harness_passed=$false;feature_assertions_removed=$false;binary_changed=$false} | ConvertTo-Json | Set-Content (Join-Path $diagnostics 'rc7-harness-adaptation.json')
 Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $root 'old')
 $oldApp=Join-Path $root 'old/SHIFT-1.0.0-rc.7-win-x64-portable'
 & powershell.exe -NoProfile -File $compatibleSmoke -AppDirectory $oldApp -DataOwner $owner -PreserveData -ExtractedArchive -DiagnosticsDirectory (Join-Path $diagnostics 'actual-rc7-baseline')
 if($LASTEXITCODE -ne 0){throw 'Actual immutable RC7 binary failed the recorded compatibility harness.'}
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
 $null=$process.Handle # Retain the live process handle for Windows PowerShell exit-code observation.
 # RC7 refuses before persisted settings/language load; its default RU display translates this known exception.
 # Compare exact immutable-source messages in both supported languages, not a generic error substring.
 $refusalTitles=@('SHIFT не смог открыть локальные данные','SHIFT could not open local data')
 $refusalReasons=@('Эта база создана более новой версией SHIFT. Обновите приложение перед открытием.','This database belongs to a newer SHIFT version. Update SHIFT before opening it.')
 $observedNames=@();$observedTitle='';$observedReason=''
 $deadline=[DateTime]::UtcNow.AddSeconds(40);$refused=$false
 while([DateTime]::UtcNow -lt $deadline){
  $process.Refresh();if($process.HasExited){throw 'Old RC7 exited without visible downgrade explanation.'}
  if($process.MainWindowHandle -ne [IntPtr]::Zero){
   try {
    $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)
    $labels=$window.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    $names=@($labels | ForEach-Object {$_.Current.Name})
    $observedNames=@($names | Select-Object -First 500)
    $titles=@($names | Where-Object {$refusalTitles -ccontains $_})
    $reasons=@($names | Where-Object {$refusalReasons -ccontains $_})
    if($titles.Count -gt 0 -and $reasons.Count -gt 0){$observedTitle=$titles[0];$observedReason=$reasons[0];$refused=$true;break}
   } catch [System.Windows.Automation.ElementNotAvailableException] { }
  }
  Start-Sleep -Milliseconds 200
 }
 # Retain actual runtime evidence even on a failed refusal probe. No UI or images are synthesized.
 @{schema_version=1;baseline_source=$source;visible_refusal=$refused;observed_title=$observedTitle;observed_reason=$observedReason;window_text=$observedNames;expected_reason_source='immutable RC7 Strings.Presentation.cs OperationalMessages';manual_acceptance=$false} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $diagnostics 'rc7-downgrade-observed-ui.json')
 if(Test-Path -LiteralPath (Join-Path $oldData 'logs')){Copy-Item -LiteralPath (Join-Path $oldData 'logs') -Destination (Join-Path $diagnostics 'rc7-downgrade-logs') -Recurse}
 Add-Type -AssemblyName System.Drawing
 Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Rc7DowngradeCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect rect);
}
'@
 $process.Refresh()
 if($process.MainWindowHandle -ne [IntPtr]::Zero){
  $rect=New-Object Rc7DowngradeCapture+Rect
  if([Rc7DowngradeCapture]::GetWindowRect($process.MainWindowHandle,[ref]$rect) -and $rect.Right -gt $rect.Left -and $rect.Bottom -gt $rect.Top){
   $bitmap=New-Object System.Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
   $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
   try{$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $diagnostics 'rc7-downgrade-refusal.png'),[System.Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
  }
 }
 if(-not $refused){throw 'Old RC7 did not visibly show the exact supported-language newer-schema refusal; inspect recorded UI evidence.'}

 if(-not $process.CloseMainWindow() -or -not $process.WaitForExit(15000) -or $process.ExitCode -ne 0){throw 'Old RC7 refusal did not exit gracefully.'}
 $process.Dispose();$process=$null
 if((Get-FileHash -LiteralPath $downgrade -Algorithm SHA256).Hash -cne $before){throw 'Old RC7 modified the downgraded v3 copy.'}
 Tool 'upgrade-rc7-check' $data
 if((Get-FileHash -LiteralPath (Join-Path $data 'shift.db') -Algorithm SHA256).Hash -cne $upgradedBefore){throw 'Original upgraded database changed during downgrade-copy acceptance.'}
 @{status='passed';source_commit=$env:GITHUB_SHA;baseline_source=$source;baseline_version='1.0.0-rc.7';baseline_portable_sha256=$actual;original_harness_sha256=$originalHarnessHash;compatible_harness_sha256=$compatibleHarnessHash;harness_adaptation='Set-Field ValuePattern readiness/readback + Quick-Key stable owned focus; two helper hunks only';original_harness_passed=$false;actual_rc7_ui_baseline=$true;actual_schema2_to3_upgrade=$true;workspace_history_settings_preserved=$true;validated_schema2_safety_backup=$true;old_rc7_visible_downgrade_refusal=$true;downgrade_observed_title=$observedTitle;downgrade_observed_reason=$observedReason;downgrade_copy_sha256_unchanged=$true;original_upgraded_profile_unchanged=$true;manual_acceptance=$false;environment='disposable Windows runner; owned portable data only'} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $diagnostics 'rc7-upgrade-smoke.json')
 Write-Host 'Actual RC7 upgrade, preservation and visible downgrade refusal passed.'
} finally {
 if($null -ne $process){$process.Refresh();if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 if((Test-Path -LiteralPath (Join-Path $root '.upgrade-owner')) -and (Get-Content -LiteralPath (Join-Path $root '.upgrade-owner') -Raw).Trim() -ceq $owner.ToString()){Remove-Item -LiteralPath $root -Recurse -Force}
}
