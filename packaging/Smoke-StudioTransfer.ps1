# Native sharing acceptance; attaches only to the exact extracted app and GUID-owned disposable data.
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
if($DataOwner -eq [guid]::Empty){throw 'A nonempty disposable data owner is required.'}
$data=(Resolve-Path -LiteralPath $DataDirectory).Path
$marker=Join-Path $data '.studio-ci-owner'
if(-not(Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne $DataOwner.ToString()){throw 'Unowned Studio test data refused.'}
$proofPath=Join-Path $data 'studio-sharing-ci.json'
if((Test-Path -LiteralPath $proofPath) -or (Test-Path -LiteralPath (Join-Path $data 'studio-sharing-before.json'))){throw 'Sharing proof already exists; refusing a duplicate test run.'}
$process=[Diagnostics.Process]::GetProcessById($ShiftProcessId)
$app=Split-Path $data -Parent
$expectedExe=[IO.Path]::GetFullPath((Join-Path $app 'SHIFT.exe'))
if(-not(Test-Path -LiteralPath (Join-Path $app 'portable.flag') -PathType Leaf) -or $process.ProcessName -ne 'SHIFT' -or -not [string]::Equals($process.MainModule.FileName,$expectedExe,[StringComparison]::OrdinalIgnoreCase)){throw 'Expected the extracted portable SHIFT.exe belonging to the owned data.'}
$repo=Split-Path $PSScriptRoot -Parent
$tool=Join-Path $repo 'tools/Shift.StudioAcceptance/bin/Release/net10.0/Shift.StudioAcceptance.dll'
if(-not(Test-Path -LiteralPath $tool -PathType Leaf)){throw 'Built Studio acceptance tool is required.'}
New-Item -Path $DiagnosticsDirectory -ItemType Directory -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class StudioSharingProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("dwmapi.dll")] public static extern int DwmFlush();
}
'@
$checks=@()
$expectedName='RC5 Перенос'
$cancelledName='RC5 Отменённый перенос'
$sourceId='00000000-0000-4000-8000-000000000104'
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
function Find([string]$Id='',[string]$Name=''){
    $root=Main-Window;if($null -eq $root){return $null}
    $property=if($Id){[System.Windows.Automation.AutomationElement]::AutomationIdProperty}else{[System.Windows.Automation.AutomationElement]::NameProperty}
    $value=if($Id){$Id}else{$Name}
    $condition=New-Object System.Windows.Automation.PropertyCondition($property,$value)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Visible([string]$Id='',[string]$Name=''){
    $element=Find $Id $Name
    if($element -and -not $element.Current.IsOffscreen){return $element}
    return $null
}
function Click([string]$Id='',[string]$Name=''){
    $element=Wait-For {$candidate=Find $Id $Name;if($candidate -and $candidate.Current.IsEnabled){$candidate}} ('button '+$Id+' '+$Name)
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Field([string]$Id,[string]$Value){
    $element=Wait-For {$candidate=Find $Id;if($candidate -and $candidate.Current.IsEnabled){$candidate}} $Id
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Read-Output([string]$Id){
    $element=Wait-For {Find $Id} $Id
    $value=$element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if(-not $value.Current.IsReadOnly){throw "Export preview is unexpectedly editable: $Id"}
    return $value.Current.Value
}
function Widget-Handles {
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$ShiftProcessId)
    return @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition) |
        Where-Object {$_.Current.Name -like 'SHIFT Виджет*' -or $_.Current.Name -like 'SHIFT Widget*'} |
        ForEach-Object {[long]$_.Current.NativeWindowHandle} | Sort-Object)
}
function Assert-Environment {
    $after=@(Widget-Handles)
    if($after.Count -ne 10 -or ($after -join ',') -ne ($script:beforeHandles -join ',')){throw 'Sharing changed or recreated the active ten widget windows.'}
}
function Tool([string]$Stage){
    $output=& dotnet $tool $Stage $data $DataOwner.ToString()
    if($LASTEXITCODE -ne 0){throw "Studio sharing acceptance tool failed: $Stage"}
    return ($output -join "`n")
}
function Capture([string]$Name){
    Start-Sleep -Milliseconds 500
    [void][StudioSharingProbe]::DwmFlush()
    $bounds=New-Object StudioSharingProbe+Rect
    $root=Wait-For {Main-Window} 'SHIFT window for sharing screenshot'
    if(-not [StudioSharingProbe]::GetWindowRect([intptr]$root.Current.NativeWindowHandle,[ref]$bounds)){throw 'Cannot capture actual SHIFT window bounds.'}
    $bitmap=New-Object Drawing.Bitmap(($bounds.Right-$bounds.Left),($bounds.Bottom-$bounds.Top))
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{$graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $DiagnosticsDirectory ('studio-sharing-'+$Name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}
    finally{$graphics.Dispose();$bitmap.Dispose()}
}
function Close-Export([string]$Id){
    Click '' 'Закрыть'
    $null=Wait-For {-not(Find $Id)} 'export dialog dismissed'
}
function Open-Import {
    Click 'Studio.Profile.Import'
    $null=Wait-For {Visible 'Studio.Pack.Input'} 'Russian profile import dialog'
}
function Review-Pack([string]$Json,[string]$Name){
    Set-Field 'Studio.Pack.Input' $Json;Click 'Studio.Pack.Review'
    $null=Wait-For {Visible 'Studio.Pack.Name'} 'validated profile preview'
    Set-Field 'Studio.Pack.Name' $Name
}
try {
    $main=Wait-For {Main-Window} 'existing SHIFT main window'
    [void][StudioSharingProbe]::ShowWindow([intptr]$main.Current.NativeWindowHandle,9)
    [void][StudioSharingProbe]::SetForegroundWindow([intptr]$main.Current.NativeWindowHandle)
    Click 'Nav.Studio';Click 'Studio.Tab.Profiles';Set-Field 'Studio.Profile.Search' ''
    $script:beforeHandles=@(Widget-Handles)
    if($beforeHandles.Count -ne 10){throw 'Expected the pre-existing ten widget windows before sharing.'}
    Click ('Studio.Profile.Export.'+$sourceId)
    $defaultJson=Read-Output 'Studio.Pack.Output'
    $defaultPack=$defaultJson | ConvertFrom-Json
    if($defaultPack.InitialNotesIncluded -ne $false -or @($defaultPack.Definitions).Count -ne 4 -or @($defaultPack.Profile.Widgets).Count -ne 4){throw 'Default native export metadata/counts are incorrect.'}
    foreach($definition in $defaultPack.Definitions){if($definition.Content.InitialNotes -ne ''){throw 'Default export leaked initial note text.'}}
    $choice=Wait-For {Find 'Studio.Pack.IncludeNotes'} 'initial notes explicit opt-in'
    $toggle=$choice.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off){throw 'Initial note export is enabled by default.'}
    $toggle.Toggle()
    $null=Wait-For {$changed=(Read-Output 'Studio.Pack.Output') | ConvertFrom-Json;$changed.InitialNotesIncluded -eq $true -and @($changed.Definitions | Where-Object {$_.Content.InitialNotes -eq 'Initial CI note'}).Count -eq 4} 'explicit initial text inclusion'
    $choice=Wait-For {Find 'Studio.Pack.IncludeNotes'} 'initial notes opt-out'
    $choice.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    $null=Wait-For {(Read-Output 'Studio.Pack.Output') -ceq $defaultJson} 'privacy default restored'
    [IO.File]::WriteAllText((Join-Path $data 'studio-sharing-export.json'),$defaultJson,(New-Object Text.UTF8Encoding($false)))
    Capture 'ru-export';Close-Export 'Studio.Pack.Output';Assert-Environment
    $checks+='profile_export_default_privacy_and_explicit_initial_text'

    Click ('Studio.Profile.Notes.'+$sourceId)
    $notes=Read-Output 'Studio.Notes.Output'
    if(-not $notes.Contains('Native CI note — Unicode')){throw 'Native notes preview did not include the saved Unicode note.'}
    [IO.File]::WriteAllText((Join-Path $data 'studio-sharing-notes.txt'),$notes,(New-Object Text.UTF8Encoding($false)))
    Capture 'ru-notes';Close-Export 'Studio.Notes.Output';Assert-Environment
    $checks+='saved_unicode_notes_export'
    $null=Tool 'sharing-before'

    Open-Import;Set-Field 'Studio.Pack.Input' '{"Format":"unsupported"}';Click 'Studio.Pack.Review'
    $null=Wait-For {Visible '' 'Профиль не импортирован'} 'localized inline import error'
    $save=Wait-For {Find '' 'Сохранить'} 'disabled primary save on invalid input'
    if($save.Current.IsEnabled){throw 'Invalid import left Save enabled.'}
    Click '' 'Отмена';$null=Wait-For {-not(Find 'Studio.Pack.Input')} 'invalid import dialog dismissed'
    $null=Tool 'sharing-readonly';Assert-Environment
    $checks+='invalid_import_inline_error_no_save'

    Open-Import;Review-Pack $defaultJson $cancelledName
    Capture 'ru-import-preview'
    Click '' 'Отмена';$null=Wait-For {-not(Find 'Studio.Pack.Name')} 'cancelled valid preview dismissed'
    $null=Tool 'sharing-readonly';Assert-Environment
    $checks+='valid_import_preview_cancel_no_writes'

    Open-Import;Review-Pack $defaultJson $expectedName
    Click '' 'Сохранить'
    $null=Wait-For {-not(Find 'Studio.Pack.Name') -and (Find '' $expectedName)} 'native imported profile saved'
    $verified=(Tool 'sharing-check') | ConvertFrom-Json
    Assert-Environment
    $checks+='profile_import_atomic_save_no_activation'
    $proof=[ordered]@{
        schema_version=1;status='passed';source_commit=$env:GITHUB_SHA;profile_id=$verified.profile_id
        profiles_added=$verified.profiles_added;definitions_added=$verified.definitions_added
        existing_data_unchanged=$verified.owner_data_unchanged;live_widget_hwnds_unchanged=$true
        runtime_state_excluded=$verified.runtime_state_excluded;unicode_notes_export_verified=$verified.unicode_notes_export_verified
        file_picker_save='not_verified';clipboard='not_verified';manual_acceptance=$false;checks=$checks
    }
    $proof | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $proofPath -Encoding UTF8
    Copy-Item -LiteralPath $proofPath -Destination (Join-Path $DiagnosticsDirectory 'studio-sharing-smoke.json')
} catch {
    try{Capture 'failure'}catch{}
    throw
} finally {
    # Deliberately leave only synthetic evidence in this GUID-owned data tree for the parent smoke cleanup.
    # Do not close the existing process, delete data, touch wallpaper or use the clipboard.
    $process.Dispose()
}
