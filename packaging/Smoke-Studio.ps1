# Actual extracted SHIFT.exe; disposable Windows CI only. No wallpaper test on an owner's profile.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppDirectory,[Parameter(Mandatory=$true)][string]$DiagnosticsDirectory)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue' # PS5 progress rendering is expensive for the verified RC2 binary download.
Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true'){throw 'Studio smoke requires disposable GitHub Actions Windows.'}
$app=(Resolve-Path $AppDirectory).Path
if(-not(Test-Path (Join-Path $app 'portable.flag'))){throw 'Extracted portable distribution required.'}
$data=Join-Path $app 'data'
if(Test-Path $data){throw 'Refusing existing data, even on CI.'}
$owner=[guid]::NewGuid()
New-Item $data -ItemType Directory | Out-Null
$owner.ToString() | Set-Content (Join-Path $data '.studio-ci-owner')
New-Item $DiagnosticsDirectory -ItemType Directory -Force | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$tool=Join-Path $repo 'tools/Shift.StudioAcceptance/bin/Release/net10.0/Shift.StudioAcceptance.dll'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class StudioProbe {
[StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
[DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr h,out uint c,out byte a,out uint f);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int cmd);
[DllImport("dwmapi.dll")] public static extern int DwmFlush();
}
'@
$process=$null;$main=$null;$proof=$null;$raw=@();$checks=@();$oldRoot=$null
function Tool([string]$Stage,[string]$Extra=''){
 $arguments=@($tool,$Stage,$data,$owner.ToString());if($Extra){$arguments+=$Extra}
 $output=& dotnet @arguments
 if($LASTEXITCODE -ne 0){throw "Studio acceptance tool failed: $Stage"}
 return ($output -join "`n")
}
function Wait-For([scriptblock]$Probe,[string]$Description){
 $end=[DateTime]::UtcNow.AddSeconds(40)
 while([DateTime]::UtcNow -lt $end){
  if($process){$process.Refresh();if($process.HasExited){throw "SHIFT exited while waiting for $Description"}}
  try{$result=& $Probe}catch [System.Windows.Automation.ElementNotAvailableException]{$result=$null}
  if($null -ne $result -and $result -ne $false){return $result}
  Start-Sleep -Milliseconds 200
 }
 throw "Timed out: $Description"
}
function Find([string]$Id='', [string]$Name='', $Root=$main){
 $property=if($Id){[System.Windows.Automation.AutomationElement]::AutomationIdProperty}else{[System.Windows.Automation.AutomationElement]::NameProperty}
 $value=if($Id){$Id}else{$Name}
 $condition=New-Object System.Windows.Automation.PropertyCondition($property,$value)
 return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Click([string]$Id='', [string]$Name='', $Root=$main){
 $control=Wait-For {$c=Find $Id $Name $Root;if($c -and $c.Current.IsEnabled){$c}} ('button '+$Id+' '+$Name)
 $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Field([string]$Id,[string]$Value){
 $control=Wait-For {Find $Id} $Id
 $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Select-Value([string]$Id,[string]$Name){
 # WinUI may replace/collapse the automation peer while the Settings page finishes layout.
 # Reacquire and idempotently expand on each probe; never retain a stale dropdown peer.
 $null=Wait-For {
  $control=Find $Id
  if(-not $control -or -not $control.Current.IsEnabled){return $null}
  $selected=$control.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
  if($selected.Count -eq 1 -and $selected[0].Current.Name -eq $Name){return $true}
  $expand=$control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
  if($expand.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded){$control.SetFocus();$expand.Expand()}
  $condition=New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$Name)),(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
  $choice=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if($choice -and $choice.Current.IsEnabled){$choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()}
  return $null
 } ('selected '+$Name)
 $control=Wait-For {Find $Id} ('choice '+$Id)
 $pattern=$control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
 if($pattern.Current.ExpandCollapseState -eq [System.Windows.Automation.ExpandCollapseState]::Expanded){$pattern.Collapse()}
}
function Idle-Button([string]$Id){$null=Wait-For {$button=Find $Id;if($button -and $button.Current.IsEnabled){$true}} ('completed '+$Id)}
function Widgets {
 $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
 return @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition) | Where-Object {($_.Current.Name -like 'SHIFT Widget*' -or $_.Current.Name -like 'SHIFT Виджет*')})
}
function Capture([string]$Name){
 Start-Sleep -Milliseconds 500
 [void][StudioProbe]::DwmFlush()
 $bounds=[System.Windows.Forms.SystemInformation]::VirtualScreen
 if($Name -notlike 'desktop-*' -and $main){
  $window=New-Object StudioProbe+Rect
  if([StudioProbe]::GetWindowRect([intptr]$main.Current.NativeWindowHandle,[ref]$window)){$bounds=New-Object Drawing.Rectangle($window.Left,$window.Top,($window.Right-$window.Left),($window.Bottom-$window.Top))}
 }
 $bitmap=New-Object Drawing.Bitmap($bounds.Width,$bounds.Height)
 $graphics=[Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $DiagnosticsDirectory ('studio-'+$Name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
}
function Sample([int]$Count){
 Start-Sleep -Seconds 2
 $process.Refresh();$cpu=$process.TotalProcessorTime.TotalMilliseconds;$start=[Diagnostics.Stopwatch]::StartNew()
 Start-Sleep -Seconds 2
 $process.Refresh();$elapsed=$start.Elapsed.TotalMilliseconds
 $script:raw+=@{windows=$Count;working_set_bytes=$process.WorkingSet64;private_bytes=$process.PrivateMemorySize64;cpu_machine_percent=100*($process.TotalProcessorTime.TotalMilliseconds-$cpu)/$elapsed/[Environment]::ProcessorCount;sample_ms=$elapsed}
 $script:raw | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $DiagnosticsDirectory 'studio-performance-raw.json')
}
function Apply([int]$Count){
 $before=@(Widgets | ForEach-Object { $_.Current.NativeWindowHandle })
 $id='00000000-0000-4000-8000-'+(100+$Count).ToString('D12')
 Click ('Studio.Profile.Apply.'+$id)
 if($Count -eq 4 -and $proof.wallpaper_eligible){Click '' 'Allow wallpaper changes'}
 $null=Wait-For {
  $current=@(Widgets); if($current.Count -eq $Count -and @($current | Where-Object {$_.Current.NativeWindowHandle -in $before}).Count -eq 0){$true}
 } "exactly $Count new widget windows"
}
try {
 Add-Type -AssemblyName System.Windows.Forms
 & dotnet restore (Join-Path $repo 'tools/Shift.StudioAcceptance') --locked-mode --nologo
 if($LASTEXITCODE -ne 0){throw 'Acceptance locked restore failed.'}
 & dotnet build (Join-Path $repo 'tools/Shift.StudioAcceptance') -c Release --no-restore --nologo
 if($LASTEXITCODE -ne 0){throw 'Acceptance tool build failed.'}
 # Published RC2 creates real schema1/history; copy via SQLite backup after a graceful exit.
 $oldRoot=Join-Path $env:RUNNER_TEMP ('SHIFT-RC2-upgrade-'+$owner.ToString('N'))
 New-Item $oldRoot -ItemType Directory | Out-Null
 $oldZip=Join-Path $oldRoot 'rc2.zip'
 Invoke-WebRequest 'https://github.com/LosarOS/SHIFT/releases/download/v1.0.0-rc.2/SHIFT-1.0.0-rc.2-win-x64-portable.zip' -OutFile $oldZip -UseBasicParsing
 if((Get-FileHash $oldZip -Algorithm SHA256).Hash.ToLowerInvariant() -ne '928545e13ec3f6b22e584723c4c3c292beb043074e7eca8f990503ed68c10a4e'){throw 'Published RC2 checksum mismatch.'}
 Expand-Archive -LiteralPath $oldZip -DestinationPath $oldRoot
 $oldApp=Join-Path $oldRoot 'SHIFT-1.0.0-rc.2-win-x64-portable'
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Windows.ps1') -AppDirectory $oldApp -DataOwner $owner -PreserveData -DiagnosticsDirectory (Join-Path $DiagnosticsDirectory 'rc2-upgrade-baseline')
 if($LASTEXITCODE -ne 0){throw 'Actual RC2 profile preparation failed.'}
 $null=Tool 'seed' (Join-Path $oldApp 'data/shift.db')
 $proof=Get-Content (Join-Path $data 'studio-ci.json') -Raw | ConvertFrom-Json
 Copy-Item (Join-Path $data 'studio-ci.json') (Join-Path $DiagnosticsDirectory 'studio-native-environment.json')
 # Hide only disposable runner console windows; keep real widget screenshots unobstructed.
 foreach($window in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)){
  if($window.Current.ClassName -in @('CASCADIA_HOSTING_WINDOW_CLASS','ConsoleWindowClass') -or ($window.Current.ClassName -eq '#32770' -and $window.Current.Name -eq 'System Properties')){[void][StudioProbe]::ShowWindow([intptr]$window.Current.NativeWindowHandle,6)}
 }
 $process=Start-Process (Join-Path $app 'SHIFT.exe') -WorkingDirectory $app -PassThru
 $main=Wait-For {
  $condition=New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)),(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SHIFT')))
  [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
 } 'main SHIFT window'
 Click 'Nav.Studio';$null=Wait-For {Find 'Studio.Enable'} 'Studio ready';Capture 'overview';Sample 0
 Click 'Studio.Enable';Click 'Studio.Tab.Widgets';Capture 'gallery'
 Click 'Studio.Widget.Edit.00000000-0000-4000-8000-000000000010'
 Set-Field 'Studio.Widget.Title' 'Cancelled draft must not persist';Click 'Studio.Widget.Preview'
 $null=Wait-For {@(Widgets).Count -eq 1} 'real desktop draft preview';Capture 'widget-lab'
 Click '' 'Cancel';$null=Wait-For {@(Widgets).Count -eq 0} 'preview cancellation'
 Click 'Studio.Widget.Edit.00000000-0000-4000-8000-000000000010';Set-Field 'Studio.Widget.Foreground' '#EEDDCC';Capture 'properties';Click '' 'Save'
 $null=Wait-For {Find 'Studio.Widget.Edit.00000000-0000-4000-8000-000000000010'} 'Lab save finished'
 $null=Tool 'lab-check';$checks+='lab_preview_cancel_save_no_preview_writes'
 Click 'Studio.Tab.Profiles';Capture 'profiles'
 Click 'Studio.Profile.Edit.00000000-0000-4000-8000-000000000104';Capture 'monitors';Click '' 'Cancel'
 Apply 4
 $notes=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like '*Notes'}) | Select-Object -First 1} 'Notes window'
 $editor=Wait-For {Find '' 'Note text, autosave' $notes} 'real Notes editor'
 $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Native CI note — Unicode')
 $focus=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like '*FocusTimer'}) | Select-Object -First 1} 'Focus timer'
 Click '' 'Start' $focus
 Start-Sleep -Seconds 6
 $inspect=Tool 'inspect';$inspect | Set-Content (Join-Path $DiagnosticsDirectory 'studio-state-check.json')
 $clock=Widgets | Where-Object {$_.Current.Name -like '*Clock'} | Select-Object -First 1
 [uint32]$key=0;[byte]$alpha=255;[uint32]$flags=0
 $layered=[StudioProbe]::GetLayeredWindowAttributes([intptr]$clock.Current.NativeWindowHandle,[ref]$key,[ref]$alpha,[ref]$flags)
 if(-not $layered -or ($flags -band 2) -eq 0 -or $alpha -lt 210 -or $alpha -gt 220){throw 'Native whole-window alpha did not match the saved .85 opacity.'}
 [void][StudioProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,6);Capture 'desktop-clock-monitor';[void][StudioProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,9)
 $checks+='four_real_windows_notes_timer'
 # Real move/resize; normalized location is saved on runtime stop and confirmed on reopen.
 $rect=New-Object StudioProbe+Rect
 [void][StudioProbe]::GetWindowRect([intptr]$clock.Current.NativeWindowHandle,[ref]$rect)
 [void][StudioProbe]::SetWindowPos([intptr]$clock.Current.NativeWindowHandle,[intptr]::Zero,80,90,300,220,0x0014)
 Click 'Studio.DesktopMode';Idle-Button 'Studio.DesktopMode';$null=Tool 'mode-check';$checks+='mode_change_preserves_geometry'
 Click 'Studio.Gather';Idle-Button 'Studio.Gather';$null=Tool 'gather-check';$checks+='recovery_gather_persists_geometry';Click 'Studio.EditMode';Idle-Button 'Studio.EditMode';Start-Sleep -Seconds 2
 Apply 4
 $notes=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like '*Notes'}) | Select-Object -First 1} 'reapplied Notes window'
 $editor=Wait-For {
  $fresh=@(Widgets | Where-Object {$_.Current.Name -like '*Notes'}) | Select-Object -First 1
  if($fresh){Find '' 'Note text, autosave' $fresh}
 } 'reapplied note editor'
 if($editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'Native CI note — Unicode'){throw 'Reapply displayed stale pre-flush note text.'}
 $checks+='reapply_no_duplicate_windows'
 Click 'Studio.Restore';Click 'Studio.Restore'
 $null=Wait-For {@(Widgets).Count -eq 0} 'widget rollback'
 $wallpaperVerified=$false
 if($proof.wallpaper_eligible){$restored=Tool 'verify-wallpaper';if($restored -ne 'WALLPAPER_RESTORED'){throw 'Native wallpaper restore not verified.'};$wallpaperVerified=$true;$checks+='wallpaper_apply_restore'}
 $null=Tool 'inspect' | Set-Content (Join-Path $DiagnosticsDirectory 'studio-state-check.json')
 foreach($count in @(1,5,10)){
  Apply $count;Sample $count
  if($count -eq 5){
   $focus=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like '*FocusTimer'}) | Select-Object -First 1} 'restart test timer'
   Click '' 'Start' $focus;Start-Sleep -Seconds 5
   Click '' 'Restart timer' $focus;Start-Sleep -Seconds 1
   $null=Tool 'timer-restart-check';Click '' 'Pause' $focus;$checks+='timer_restart_after_completion'
  }
 }
 # Change language through the actual Settings UI while ten native widgets are alive.
 $beforeRu=@(Widgets | ForEach-Object {$_.Current.NativeWindowHandle} | Sort-Object)
 Click 'Nav.Settings';Select-Value 'Field.Language' 'Русский';Click '' 'Save'
 $null=Wait-For {$nav=Find 'Nav.Studio';if($nav -and $nav.Current.Name -eq 'Студия'){$true}} 'Russian navigation'
 Click 'Nav.Studio';Click 'Studio.Tab.Overview';Capture 'ru-overview'
 $null=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like 'SHIFT Виджет*'}).Count -eq 10} 'live Russian widget titles'
 $afterRu=@(Widgets | ForEach-Object {$_.Current.NativeWindowHandle} | Sort-Object)
 if(($beforeRu -join ',') -ne ($afterRu -join ',')){throw 'Language change recreated native widget HWNDs.'}
 $monitor=Widgets | Where-Object {$_.Current.Name -like '*SystemMonitor'} | Select-Object -First 1
 $null=Wait-For {$metric=Find 'Widget.Value' '' $monitor;if($metric -and $metric.Current.Name -like 'ЦП*'){$true}} 'Russian metric name'
 $checks+='russian_live_language_without_recreation'
 Click 'Studio.Tab.Widgets';Set-Field 'Studio.Widget.Search' 'точно нет такого виджета'
 $null=Wait-For {Find '' 'Ничего не найдено'} 'Russian empty filter'
 Set-Field 'Studio.Widget.Search' '';Capture 'ru-gallery'
 # Opening valid extreme/custom/disconnected values and switching language must not coerce them.
 Click 'Studio.Widget.Edit.00000000-0000-4000-8000-000000000014'
 Click 'Studio.Widget.Language';$null=Wait-For {Find '' 'Widget Lab'} 'English retained editor'
 Click 'Studio.Widget.Language';$null=Wait-For {Find '' 'Мастерская виджетов'} 'Russian retained editor';Capture 'ru-widget-lab'
 Click '' 'Сохранить';$null=Wait-For {Find 'Studio.Widget.Search'} 'boundary edit completed'
 $null=Tool 'editor-preserve-check';$checks+='editor_preserves_boundaries_custom_style_and_missing_monitor'
 Click 'Studio.Tab.Overview';Click 'Studio.Preset.Create.Focus';Capture 'ru-preset';Click '' 'Отмена'
 $null=Tool 'preset-cancel-check';Click 'Studio.Preset.Create.Focus';Set-Field 'Studio.Preset.Name' 'RC4 Фокус';Click '' 'Сохранить'
 $null=Wait-For {Find '' 'RC4 Фокус'} 'saved Russian preset'
 $preset=(Tool 'preset-check') | ConvertFrom-Json
 if(@(Widgets).Count -ne 10){throw 'Saving a preset changed live widget windows.'}
 Click ('Studio.Profile.Apply.'+$preset.profile_id)
 $null=Wait-For {@(Widgets).Count -eq 3} 'three actual starter widgets'
 [void][StudioProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,6);Capture 'desktop-ru-preset';[void][StudioProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,9)
 $checks+='starter_preset_cancel_atomic_save_and_apply'
 Apply 10
 & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-QuickSetup.ps1') -ShiftProcessId $process.Id -DataDirectory $data -DataOwner $owner -DiagnosticsDirectory $DiagnosticsDirectory
 if($LASTEXITCODE -ne 0){throw 'Russian Quick Setup acceptance failed.'}
 $null=Tool 'quick-check';$checks+='russian_quick_setup_order_save_cancel'
 Click 'Nav.Studio'

 # An explicitly disabled active profile must not reopen widgets on restart.
 Click 'Studio.Tab.Profiles';Click 'Studio.Profile.Edit.00000000-0000-4000-8000-000000000110'
 $enabled=Wait-For {Find '' 'Профиль включён'} 'profile enabled toggle'
 $enabled.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle();Click '' 'Сохранить'
 $null=Wait-For { -not (Find '' 'Визуальный профиль') } 'profile dialog save and close completed'
 $null=Wait-For {@(Widgets).Count -eq 0} 'disabled profile closes owned windows'
 [void][StudioProbe]::PostMessage([intptr]$main.Current.NativeWindowHandle,0x0010,[intptr]::Zero,[intptr]::Zero)
 if(-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0){throw 'Disabled-profile close failed.'}
 $process.Dispose();$process=Start-Process (Join-Path $app 'SHIFT.exe') -WorkingDirectory $app -PassThru
 $main=Wait-For {
  $condition=New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)),(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SHIFT')))
  [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
 } 'disabled-profile restart'
 Click 'Nav.Studio';Click 'Studio.Tab.Settings';$null=Wait-For {Find 'Studio.Toggle'} 'ready Studio after restart'
 Start-Sleep -Seconds 2
 if(@(Widgets).Count -ne 0){throw 'A disabled profile reopened widgets after restart.'}
 Click 'Studio.Tab.Profiles';Click 'Studio.Profile.Edit.00000000-0000-4000-8000-000000000110'
 $enabled=Wait-For {Find '' 'Профиль включён'} 're-enable profile'
 $enabled.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle();Click '' 'Сохранить'
 $null=Wait-For { -not (Find '' 'Визуальный профиль') } 'profile dialog save and close completed'
 $null=Wait-For {Find 'Studio.Profile.Apply.00000000-0000-4000-8000-000000000110'} 'profile saved'
 Apply 10;$checks+='disabled_profile_stays_disabled_after_restart'
 Click 'Studio.Tab.Settings';Capture 'settings-recovery';Click 'Studio.Disable'
 $null=Wait-For {@(Widgets).Count -eq 0} 'Studio disabled'
 Click 'Studio.Toggle';$null=Wait-For {@(Widgets).Count -eq 10} 'Studio re-enabled'
 $checks+='disable_reenable'
 [void][StudioProbe]::PostMessage([intptr]$main.Current.NativeWindowHandle,0x0010,[intptr]::Zero,[intptr]::Zero)
 if(-not $process.WaitForExit(15000)){throw 'SHIFT/widget host did not exit.'}
 if($process.ExitCode -ne 0){throw "SHIFT/widget host exited with $($process.ExitCode)."}
 $process.Dispose();$process=Start-Process (Join-Path $app 'SHIFT.exe') -WorkingDirectory $app -PassThru
 $null=Wait-For {@(Widgets).Count -eq 10} 'widget restart without duplicate windows'
 $checks+='restart_persistence_and_quit'
 $null=Wait-For {@(Widgets | Where-Object {$_.Current.Name -like 'SHIFT Виджет*'}).Count -eq 10} 'persisted Russian widgets'
 # The old main element belongs to the old process; reacquire before final close.
 $main=Wait-For {
  $condition=New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)),(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SHIFT')))
  [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
 } 'restarted main window'
 [void][StudioProbe]::PostMessage([intptr]$main.Current.NativeWindowHandle,0x0010,[intptr]::Zero,[intptr]::Zero)
 if(-not $process.WaitForExit(15000)){throw 'Restarted SHIFT did not exit cleanly.'}
 if($process.ExitCode -ne 0){throw "Restarted SHIFT exited with $($process.ExitCode)."}
 $errors=Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' | Select-String -Pattern '\sERROR\s'
 if($errors){throw 'Unexpected application ERROR during native Studio acceptance.'}
 Copy-Item (Join-Path $data 'logs') (Join-Path $DiagnosticsDirectory 'studio-app-logs') -Recurse -Force
 $raw | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $DiagnosticsDirectory 'studio-performance-raw.json')
 @{status='passed';source_commit=$env:GITHUB_SHA;environment='disposable Windows Server runner, actual extracted SHIFT.exe';checks=$checks;wallpaper_verified=$wallpaperVerified;alpha_attribute_verified=[bool]$layered;actual_alpha=[int]$alpha;manual_acceptance=$false;multi_monitor_verified=$false;sleep_verified=$false;physical_windows11_verified=$false;not_measured=@('actual_sleep_resume','widget_lab_open_latency','full_profile_apply_latency')} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $DiagnosticsDirectory 'studio-smoke.json')
} catch {
 try{
  if($main){
   Capture 'failure'
   $names=@($main.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {$_.Current.Name} | Where-Object {$_ -match 'сохран|измен|ошиб|не найден|поврежд|invalid|changed|failed'} | Select-Object -First 40)
   $names | Set-Content (Join-Path $DiagnosticsDirectory 'studio-ui-failure.txt')
   Write-Host ($names -join ' | ')
  }
 }catch{}
 if(Test-Path (Join-Path $data 'logs')){Copy-Item (Join-Path $data 'logs') (Join-Path $DiagnosticsDirectory 'studio-app-logs') -Recurse}
 throw
} finally {
 if($process){$process.Refresh();if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 # Never delete wallpaper resources until current desktop state is restored and independently compared.
 if($proof){$cleanup=Tool 'cleanup';$cleanup | Set-Content (Join-Path $DiagnosticsDirectory 'studio-wallpaper-cleanup.txt')}
 if((Test-Path (Join-Path $data '.studio-ci-owner')) -and (Get-Content (Join-Path $data '.studio-ci-owner') -Raw).Trim() -eq $owner.ToString()){Remove-Item $data -Recurse -Force}
 if($oldRoot -and (Test-Path $oldRoot)){Remove-Item $oldRoot -Recurse -Force}
}
