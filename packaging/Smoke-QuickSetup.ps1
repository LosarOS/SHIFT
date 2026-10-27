# Disposable native Quick Setup acceptance; attaches to an existing owned SHIFT process.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$ShiftProcessId,
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [Parameter(Mandatory=$true)][guid]$DataOwner,
    [Parameter(Mandatory=$true)][string]$DiagnosticsDirectory
)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
Set-StrictMode -Version Latest
if($env:GITHUB_ACTIONS -ne 'true' -or [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT){throw 'Disposable GitHub Actions Windows only.'}
if($DataOwner -eq [guid]::Empty){throw 'A nonempty test data owner is required.'}
$data=(Resolve-Path -LiteralPath $DataDirectory).Path
$marker=Join-Path $data '.studio-ci-owner'
if(-not(Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne $DataOwner.ToString()){throw 'Unowned Studio test data refused.'}
$proofPath=Join-Path $data 'quick-setup-ci.json'
if(Test-Path -LiteralPath $proofPath){throw 'Quick Setup proof already exists; refusing a duplicate test run.'}
$process=[Diagnostics.Process]::GetProcessById($ShiftProcessId)
$expectedExe=[IO.Path]::GetFullPath((Join-Path (Split-Path $data -Parent) 'SHIFT.exe'))
if($process.ProcessName -ne 'SHIFT' -or -not [string]::Equals($process.MainModule.FileName,$expectedExe,[StringComparison]::OrdinalIgnoreCase)){throw 'Expected the extracted SHIFT.exe belonging to the owned portable data.'}
New-Item -Path $DiagnosticsDirectory -ItemType Directory -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class QuickSetupProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("dwmapi.dll")] public static extern int DwmFlush();
}
'@
$fixtureDirectory=$null
$shell=$null
$expectedName='RC4 Быстрое пространство'
$cancelledName='RC4 Отменено '+$DataOwner.ToString('N').Substring(0,10)
$searchToken='SHIFT RC4 '+$DataOwner.ToString('N').Substring(0,10)
$checks=@()
function Wait-For([scriptblock]$Probe,[string]$Description){
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    while([DateTime]::UtcNow -lt $deadline){
        $process.Refresh();if($process.HasExited){throw "SHIFT exited while waiting for $Description"}
        try{$found=& $Probe}catch [System.Windows.Automation.ElementNotAvailableException]{$found=$null}
        if($null -ne $found -and $found -ne $false){return $found}
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out: $Description"
}
function Main-Window {
    $condition=New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$ShiftProcessId)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'SHIFT')))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,$condition)
}
function Find([string]$Id='', [string]$Name=''){
    $root=Main-Window;if($null -eq $root){return $null}
    $property=if($Id){[System.Windows.Automation.AutomationElement]::AutomationIdProperty}else{[System.Windows.Automation.AutomationElement]::NameProperty}
    $value=if($Id){$Id}else{$Name}
    $condition=New-Object System.Windows.Automation.PropertyCondition($property,$value)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Visible([string]$Id='', [string]$Name=''){
    $element=Find $Id $Name
    if($element -and -not $element.Current.IsOffscreen){return $element}
    return $null
}
function Click([string]$Id='', [string]$Name=''){
    $element=Wait-For {$candidate=Find $Id $Name;if($candidate -and $candidate.Current.IsEnabled){$candidate}} ('button '+$Id+' '+$Name)
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Field([string]$Id,[string]$Value){
    $element=Wait-For {$candidate=Find $Id;if($candidate -and $candidate.Current.IsEnabled){$candidate}} $Id
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Candidate-Id([string]$Path){
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Path.ToUpperInvariant()))).Replace('-','')}
    finally{$sha.Dispose()}
}
function Candidate-Choices {
    $root=Main-Window;if($null -eq $root){return @()}
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::CheckBox)
    return @($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {$_.Current.AutomationId.StartsWith('Setup.App.',[StringComparison]::Ordinal)})
}
function Select-Candidate([string]$Id){
    $element=Wait-For {Find ('Setup.App.'+$Id)} 'fixture app checkbox'
    $toggle=$element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off){$toggle.Toggle()}
    $null=Wait-For {$fresh=Find ('Setup.App.'+$Id);$fresh -and $fresh.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On} 'explicit app selection'
}
function Capture([string]$Name){
    Start-Sleep -Milliseconds 500
    [void][QuickSetupProbe]::DwmFlush()
    $window=New-Object QuickSetupProbe+Rect
    $root=Wait-For {Main-Window} 'SHIFT window for screenshot'
    if(-not [QuickSetupProbe]::GetWindowRect([intptr]$root.Current.NativeWindowHandle,[ref]$window)){throw 'Cannot capture real SHIFT window bounds.'}
    $bitmap=New-Object Drawing.Bitmap(($window.Right-$window.Left),($window.Bottom-$window.Top))
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{$graphics.CopyFromScreen($window.Left,$window.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $DiagnosticsDirectory ('quick-setup-'+$Name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}
    finally{$graphics.Dispose();$bitmap.Dispose()}
}
try {
    $programs=[Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if([string]::IsNullOrWhiteSpace($programs) -or -not(Test-Path -LiteralPath $programs -PathType Container)){throw 'User Start Menu is unavailable on this disposable runner.'}
    $fixtureDirectory=Join-Path $programs ('SHIFT RC4 Quick Setup '+$DataOwner.ToString('N'))
    if(Test-Path -LiteralPath $fixtureDirectory){throw 'Refusing an existing shortcut fixture directory.'}
    New-Item -Path $fixtureDirectory -ItemType Directory | Out-Null
    $DataOwner.ToString() | Set-Content -LiteralPath (Join-Path $fixtureDirectory '.shift-quick-owner') -Encoding UTF8
    $notepad=Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)) 'System32\notepad.exe'
    if(-not(Test-Path -LiteralPath $notepad -PathType Leaf)){throw 'Harmless Notepad fixture target is unavailable.'}
    $nameA=$searchToken+' A — Блокнот'
    $nameB=$searchToken+' B — Блокнот'
    $pathA=Join-Path $fixtureDirectory ($nameA+'.lnk')
    $pathB=Join-Path $fixtureDirectory ($nameB+'.lnk')
    $shell=New-Object -ComObject WScript.Shell
    foreach($path in @($pathA,$pathB)){
        $shortcut=$shell.CreateShortcut($path)
        try{$shortcut.TargetPath=$notepad;$shortcut.Arguments='';$shortcut.WorkingDirectory=Split-Path $notepad -Parent;$shortcut.Description='Owned disposable SHIFT Quick Setup fixture; never execute.';$shortcut.Save()}
        finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)}
    }
    $idA=Candidate-Id $pathA;$idB=Candidate-Id $pathB
    $main=Wait-For {Main-Window} 'existing SHIFT main window'
    [void][QuickSetupProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,9)
    [void][QuickSetupProbe]::SetForegroundWindow([intptr]$main.Current.NativeWindowHandle)
    Click 'Nav.Workspaces';Click 'Workspace.QuickSetup'
    Set-Field 'Setup.Search' $searchToken
    $null=Wait-For {@(Candidate-Choices).Count -eq 2} 'exactly two owned shortcut choices'
    # Selection order deliberately differs from catalog order; preview changes it back to A,B.
    Select-Candidate $idB;Select-Candidate $idA;Capture 'ru-selection'
    Click '' 'Далее: название и предпросмотр'
    $null=Wait-For {Visible 'Setup.Name'} 'RU preview step'
    $null=Wait-For {Visible '' ('1.  '+$nameB)} 'explicit B-first preview order'
    Click ('Setup.Up.'+$idA)
    $null=Wait-For {Visible '' ('1.  '+$nameA)} 'reordered A-first preview'
    Set-Field 'Setup.Name' $expectedName
    Capture 'ru-preview'
    Click '' 'Назад к выбору'
    $search=Wait-For {Visible 'Setup.Search'} 'selection step after Back'
    if($search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $searchToken){throw 'Back lost the search query.'}
    foreach($id in @($idA,$idB)){
        $choice=Wait-For {Find ('Setup.App.'+$id)} 'retained selection'
        if($choice.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On){throw 'Back lost an explicit choice.'}
    }
    Click '' 'Далее: название и предпросмотр'
    $field=Wait-For {Visible 'Setup.Name'} 'returned preview'
    if($field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $expectedName){throw 'Back lost the workspace name draft.'}
    $null=Wait-For {Visible '' ('1.  '+$nameA)} 'order retained across Back'
    Click '' 'Сохранить пространство'
    $null=Wait-For {(Find 'Item.New') -and (Visible '' $expectedName)} 'saved workspace detail'
    $checks+='search_select_preview_reorder_back_save_ru'
    # Cancel a second nonempty draft; it must not create a second workspace.
    Click 'Nav.Workspaces';Click 'Workspace.QuickSetup'
    Set-Field 'Setup.Search' $searchToken
    $null=Wait-For {@(Candidate-Choices).Count -eq 2} 'second discovery result'
    Select-Candidate $idA
    Click '' 'Далее: название и предпросмотр'
    Set-Field 'Setup.Name' $cancelledName
    Click '' 'Отмена'
    $null=Wait-For {if(-not(Visible 'Setup.Name') -and (Visible 'Workspace.QuickSetup') -and (Visible '' $expectedName)){$true}} 'cancelled wizard dismissal and saved workspace retained'
    if(Visible '' $cancelledName){throw 'Cancelled workspace draft appeared in the workspace list.'}
    $checks+='nonempty_draft_cancel_ru'
    $proof=[ordered]@{schema_version=1;status='passed';source_commit=$env:GITHUB_SHA;expectedName=$expectedName;cancelledName=$cancelledName;targetPaths=@($pathA,$pathB);expectedItemCount=2;checks=$checks;shortcuts_executed=$false;manual_acceptance=$false}
    $proof | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $proofPath -Encoding UTF8
    Copy-Item -LiteralPath $proofPath -Destination (Join-Path $DiagnosticsDirectory 'quick-setup-smoke.json')
} catch {
    try{Capture 'failure'}catch{}
    throw
} finally {
    if($shell){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
    # Never delete data, apps, workspaces or an unowned Start Menu entry.
    if($fixtureDirectory -and (Test-Path -LiteralPath $fixtureDirectory)){
        $fixtureMarker=Join-Path $fixtureDirectory '.shift-quick-owner'
        if(-not(Test-Path -LiteralPath $fixtureMarker -PathType Leaf) -or (Get-Content -LiteralPath $fixtureMarker -Raw).Trim() -ne $DataOwner.ToString()){throw 'Shortcut cleanup refused an unowned directory.'}
        Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force
    }
    $process.Dispose()
}
