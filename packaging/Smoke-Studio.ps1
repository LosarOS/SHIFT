# Actual extracted SHIFT.exe; disposable Windows CI only. No wallpaper test on an owner's profile.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppDirectory,[Parameter(Mandatory=$true)][string]$DiagnosticsDirectory)
$ErrorActionPreference='Stop'
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
function Widgets {
 $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
 return @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition) | Where-Object {$_.Current.Name -like 'SHIFT Widget*'})
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
  if($window.Current.ClassName -in @('CASCADIA_HOSTING_WINDOW_CLASS','ConsoleWindowClass')){[void][StudioProbe]::ShowWindow([intptr]$window.Current.NativeWindowHandle,6)}
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
 Click 'Studio.Gather';Click 'Studio.EditMode';Start-Sleep -Seconds 2
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
 foreach($count in @(1,5,10)){Apply $count;Sample $count}
 Click 'Studio.Tab.Settings';Capture 'settings-recovery';Click 'Studio.Disable'
 $null=Wait-For {@(Widgets).Count -eq 0} 'Studio disabled'
 Click 'Studio.Toggle';$null=Wait-For {@(Widgets).Count -eq 10} 'Studio re-enabled'
 $checks+='disable_reenable'
 [void][StudioProbe]::PostMessage([intptr]$main.Current.NativeWindowHandle,0x0010,[intptr]::Zero,[intptr]::Zero)
 if(-not $process.WaitForExit(15000)){throw 'SHIFT/widget host did not exit.'}
 $process.Dispose();$process=Start-Process (Join-Path $app 'SHIFT.exe') -WorkingDirectory $app -PassThru
 $null=Wait-For {@(Widgets).Count -eq 10} 'widget restart without duplicate windows'
 $checks+='restart_persistence_and_quit'
 # The old main element belongs to the old process; reacquire before final close.
 $main=Wait-For {
  $condition=New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)),(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SHIFT')))
  [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
 } 'restarted main window'
 [void][StudioProbe]::PostMessage([intptr]$main.Current.NativeWindowHandle,0x0010,[intptr]::Zero,[intptr]::Zero)
 if(-not $process.WaitForExit(15000)){throw 'Restarted SHIFT did not exit cleanly.'}
 $raw | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $DiagnosticsDirectory 'studio-performance-raw.json')
 @{status='passed';source_commit=$env:GITHUB_SHA;environment='disposable Windows Server runner, actual extracted SHIFT.exe';checks=$checks;wallpaper_verified=$wallpaperVerified;alpha_attribute_verified=[bool]$layered;actual_alpha=[int]$alpha;manual_acceptance=$false;multi_monitor_verified=$false;sleep_verified=$false;physical_windows11_verified=$false;not_measured=@('actual_sleep_resume','widget_lab_open_latency','full_profile_apply_latency')} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $DiagnosticsDirectory 'studio-smoke.json')
} catch {
 try{if($main){Capture 'failure'}}catch{}
 if(Test-Path (Join-Path $data 'logs')){Copy-Item (Join-Path $data 'logs') (Join-Path $DiagnosticsDirectory 'studio-app-logs') -Recurse}
 throw
} finally {
 if($process){$process.Refresh();if(-not $process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 # Never delete wallpaper resources until current desktop state is restored and independently compared.
 if($proof){$cleanup=Tool 'cleanup';$cleanup | Set-Content (Join-Path $DiagnosticsDirectory 'studio-wallpaper-cleanup.txt')}
 if((Test-Path (Join-Path $data '.studio-ci-owner')) -and (Get-Content (Join-Path $data '.studio-ci-owner') -Raw).Trim() -eq $owner.ToString()){Remove-Item $data -Recurse -Force}
 if($oldRoot -and (Test-Path $oldRoot)){Remove-Item $oldRoot -Recurse -Force}
}
