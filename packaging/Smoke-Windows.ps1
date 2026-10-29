# Windows PowerShell 5.1 + UI Automation. Uses only disposable data created by this script.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$AppDirectory,
    [string]$DiagnosticsDirectory = '',
    [switch]$Installed,
    [guid]$DataOwner = [guid]::Empty,
    [switch]$PreserveData,
    [switch]$ResumeOwnedData,
    [switch]$ExtractedArchive
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShiftWindowProbe {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("dwmapi.dll")] public static extern int DwmFlush();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$app = (Resolve-Path $AppDirectory).Path
$exe = Join-Path $app 'SHIFT.exe'
$data = if ($Installed) { Join-Path $env:LOCALAPPDATA 'SHIFT' } else { Join-Path $app 'data' }
if ($Installed -and $ExtractedArchive) { throw 'Installed smoke cannot claim portable archive validation.' }
if ($Installed -and $env:GITHUB_ACTIONS -ne 'true') { throw 'Installed smoke only runs on an isolated GitHub Actions runner.' }
if (-not $Installed -and -not (Test-Path (Join-Path $app 'portable.flag'))) { throw 'Expected a disposable portable build.' }
$ownerFile = Join-Path $data '.shift-ci-owner'
if ($PreserveData -or $ResumeOwnedData) {
    if ($env:GITHUB_ACTIONS -ne 'true' -or $DataOwner -eq [guid]::Empty) { throw 'Data reuse requires an explicit owner token on a disposable CI runner.' }
}
if (Test-Path $data) {
    if (-not $ResumeOwnedData -or -not (Test-Path $ownerFile) -or (Get-Content $ownerFile -Raw).Trim() -ne $DataOwner.ToString()) {
        throw 'Refusing to use or remove existing user data.'
    }
} elseif ($ResumeOwnedData) { throw 'Owned data to resume is missing.' }
else {
    New-Item $data -ItemType Directory | Out-Null
    if ($DataOwner -ne [guid]::Empty) { $DataOwner.ToString() | Set-Content $ownerFile }
}
if ($DiagnosticsDirectory) { New-Item $DiagnosticsDirectory -ItemType Directory -Force | Out-Null }
$process = $null
$monitor = $null
$dumpDirectory = $null
$startedAt = Get-Date
$label = if ($Installed) { 'installed' } else { 'portable' }
function Wait-For([scriptblock]$Probe,[string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "SHIFT exited ($($process.ExitCode)) waiting for $Description" }
        try { $result = & $Probe } catch [System.Windows.Automation.ElementNotAvailableException] { $result = $null }
        if ($null -ne $result -and $result -ne $false) { return $result }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $Description"
}
function Find-Control([string]$Name,[System.Windows.Automation.ControlType]$Type,[string]$Id='',[switch]$Desktop) {
    $process.Refresh()
    if ($Desktop) { $scope = [System.Windows.Automation.AutomationElement]::RootElement }
    elseif ($process.MainWindowHandle -ne [IntPtr]::Zero) { $scope = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
    else { return $null }
    $identity = if ($Id) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $value = if ($Id) { $Id } else { $Name }
    $a = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @($identity,$value)
    $b = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::ControlTypeProperty,$Type)
    $condition = New-Object System.Windows.Automation.AndCondition -ArgumentList @($a,$b)
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
}
function Click([string]$Name,[string]$Id='') {
    $control = Wait-For {
        $candidate=Find-Control $Name ([System.Windows.Automation.ControlType]::Button) $Id
        if($null -ne $candidate -and $candidate.Current.IsEnabled){$candidate}
    } "button $Name $Id"
    $control.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Set-Field([string]$Id,[string]$Value) {
    $control = Wait-For { Find-Control '' ([System.Windows.Automation.ControlType]::Edit) $Id } "field $Id"
    $control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
}
function Quick-Selection {
    $list = Find-Control '' ([System.Windows.Automation.ControlType]::List) 'Quick.Results'
    if ($null -eq $list) { return $null }
    $pattern = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    if ($pattern.Current.CanSelectMultiple) { throw 'Compact launcher incorrectly allows multiple selected workspaces.' }
    $selected = @($pattern.Current.GetSelection())
    if ($selected.Count -eq 1) { return $selected[0] }
    return $null
}
function History-Count {
    $scope = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $condition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Text)
    $values = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)
    foreach ($value in $values) {
        if ($value.Current.Name -cmatch '^Device local time  ·  ([0-9]+) runs in history$') { return [int]$Matches[1] }
    }
    return $null
}
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
function Select-Choice([string]$Id,[string]$Value) {
    Write-Host "Select $Id = $Value"
    # WinUI can replace automation peers while laying out a just-opened dialog.
    # Expansion/selection are idempotent; reacquire stale peers inside the bounded wait.
    $null = Wait-For {
        $control=Find-Control '' ([System.Windows.Automation.ControlType]::ComboBox) $Id
        if($control -and $control.Current.IsEnabled){
            $pattern=$control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded){$pattern.Expand()}
            $true
        }
    } "expanded choice $Id"
    $null = Wait-For {
        $choice=Find-Control $Value ([System.Windows.Automation.ControlType]::ListItem) '' -Desktop
        if($choice -and $choice.Current.IsEnabled){$choice.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select();$true}
    } "selected option $Id = $Value"
    $null = Wait-For {
        $control=Find-Control '' ([System.Windows.Automation.ControlType]::ComboBox) $Id
        if($control){
            $pattern=$control.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
            if($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Collapsed){$pattern.Collapse()}
            $true
        }
    } "collapsed choice $Id"
}
function Capture([string]$Name) {
    if (-not $DiagnosticsDirectory -or $null -eq $process) { return }
    # UI Automation observes logical state before WinUI/DWM presents the frame.
    # Wait for short Fluent transitions, then flush composition before reading genuine pixels.
    Start-Sleep -Milliseconds 750
    if ([ShiftWindowProbe]::DwmFlush() -lt 0) { throw 'Desktop composition did not flush before screenshot capture.' }
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) { return }
    $rect = New-Object ShiftWindowProbe+Rect
    if (-not [ShiftWindowProbe]::GetWindowRect($process.MainWindowHandle,[ref]$rect)) { return }
    $width = $rect.Right-$rect.Left; $height=$rect.Bottom-$rect.Top
    if ($width -le 0 -or $height -le 0) { return }
    $bitmap = New-Object Drawing.Bitmap -ArgumentList @($width,$height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size)
        $bitmap.Save((Join-Path $DiagnosticsDirectory "$label-$Name.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    if ($env:GITHUB_ACTIONS -eq 'true' -and $env:SHIFT_CRASH_MONITOR) {
        $dumpDirectory = Join-Path $env:RUNNER_TEMP ('SHIFT-dumps-' + [guid]::NewGuid().ToString('N'))
        New-Item $dumpDirectory -ItemType Directory | Out-Null
        $monitor = Start-Process $env:SHIFT_CRASH_MONITOR -ArgumentList @('-accepteula','-e','-ma','-w','SHIFT.exe',"`"$dumpDirectory`"") -PassThru -RedirectStandardOutput (Join-Path $dumpDirectory 'monitor.log') -RedirectStandardError (Join-Path $dumpDirectory 'monitor-error.log')
    }
    $process = Start-Process $exe -WorkingDirectory $app -PassThru
    if (-not $ResumeOwnedData) {
        Click 'Начать с чистого листа'
        # Two explicit owned fixtures are needed to prove selection actually changes with arrows.
        # The keyboard-only target has no items and is never dispatched.
        Click '' 'Workspace.New'
        Set-Field 'Field.Name' 'SHIFT smoke keyboard target'
        Set-Field 'Field.Description' 'Keyboard navigation fixture'
        Click 'Сохранить'
        $null = Wait-For { Find-Control 'SHIFT smoke keyboard target' ([System.Windows.Automation.ControlType]::Text) } 'saved keyboard fixture'
        Click '' 'Nav.Workspaces'
        Click '' 'Workspace.New'
        Set-Field 'Field.Name' 'SHIFT smoke workspace'
        Set-Field 'Field.Description' 'Disposable launcher diagnostic context'
        Click 'Сохранить'
        $null = Wait-For { Find-Control 'SHIFT smoke workspace' ([System.Windows.Automation.ControlType]::Text) } 'saved workspace'
        Click '' 'Item.New'
        Set-Field 'Field.Name' 'Explicit command proof'
        Select-Choice 'Field.Kind' 'Команда ⚠'
        $marker = Join-Path $data 'dispatch-proof.txt'
        $escaped = $marker.Replace("'","''")
        Set-Field 'Field.Target' "[System.IO.File]::WriteAllText('$escaped', 'SHIFT smoke')"
        Click 'Сохранить'
        $null = Wait-For { $null -eq (Find-Control 'Сохранить' ([System.Windows.Automation.ControlType]::Button)) } 'item dialog closed'
        $null = Wait-For { Find-Control 'Explicit command proof' ([System.Windows.Automation.ControlType]::Text) } 'saved item detail'
        Capture 'workspace-detail'
        Click '' 'Workspace.Doctor'
        $null = Wait-For {
            $summary=Find-Control '' ([System.Windows.Automation.ControlType]::Text) 'Doctor.Summary'
            if($summary -and $summary.Current.Name -match 'проверено локально'){$summary}
        } 'read-only workspace diagnosis'
        if (Test-Path $marker) { throw 'Doctor executed the command.' }
        Capture 'workspace-doctor'
        Click 'Проверить снова'
        $null = Wait-For {
            $summary=Find-Control '' ([System.Windows.Automation.ControlType]::Text) 'Doctor.Summary'
            if($summary -and $summary.Current.Name -match 'проверено локально'){$summary}
        } 'repeated workspace diagnosis'
        if (Test-Path $marker) { throw 'Doctor executed the command on repeat.' }
        Click 'Закрыть'
        Click '' 'Workspace.Launch'
        $null = Wait-For { Find-Control 'Разрешить и запустить' ([System.Windows.Automation.ControlType]::Button) } 'command consent'
        Capture 'command-consent'
        Click 'Отмена'
        $null = Wait-For { Find-Control 'Команды отклонены' ([System.Windows.Automation.ControlType]::Text) } 'declined queue'
        if (Test-Path $marker) { throw 'Command executed after consent was declined.' }
        Click '' 'Launch.Retry'
        Click 'Разрешить и запустить'
        $null = Wait-For { Test-Path $marker } 'explicitly approved command marker'
        if ((Get-Content $marker -Raw) -ne 'SHIFT smoke') { throw 'Command marker content mismatch.' }
        $null = Wait-For { Find-Control 'Запросы отправлены' ([System.Windows.Automation.ControlType]::Text) } 'completed queue'
        Click '' 'Nav.Settings'
        Select-Choice 'Field.Theme' 'Полночь'
        Select-Choice 'Field.Language' 'English'
        Click 'Сохранить'
        $null = Wait-For { Find-Control 'Overview' ([System.Windows.Automation.ControlType]::Button) } 'localized settings rebuild'
        Click '' 'Nav.History'
        $historyBeforeReview = Wait-For { History-Count } 'history count before compact review'
        $markerBeforeReview = Get-FileHash -LiteralPath $marker -Algorithm SHA256
        $markerWriteBeforeReview = (Get-Item -LiteralPath $marker).LastWriteTimeUtc
        Click '' 'Quick.Open'
        Set-Field 'Quick.Search' 'SHIFT smoke'
        $null = Wait-For { Find-Control '↗  SHIFT smoke workspace' ([System.Windows.Automation.ControlType]::Text) } 'quick launcher search'
        $firstSelection = Wait-For { Quick-Selection } 'single accessible compact selection'
        if ($firstSelection.Current.Name -cnotlike 'SHIFT smoke workspace.*') { throw 'Recently dispatched workspace was not initially selected.' }
        $firstSelectionId = $firstSelection.Current.AutomationId
        if ($firstSelectionId -cnotmatch '^Quick\.Workspace\.[0-9a-f]{32}$') { throw 'Selected compact workspace lacks a stable automation identity.' }
        $null = Wait-For { Find-Control 'Matching workspaces: 2' ([System.Windows.Automation.ControlType]::Text) 'Quick.ResultCount' } 'two compact fixture results'
        Capture 'quick-launcher'
        Quick-Key '{DOWN}'
        $null = Wait-For { $choice = Quick-Selection; if ($choice -and $choice.Current.Name -clike 'SHIFT smoke keyboard target.*') { $choice } } 'keyboard Down changes selected workspace'
        Quick-Key '{UP}'
        $null = Wait-For { $choice = Quick-Selection; if ($choice -and $choice.Current.AutomationId -ceq $firstSelectionId) { $choice } } 'keyboard Up restores selected workspace'
        Set-Field 'Quick.Search' '   '
        $null = Wait-For { Find-Control 'Matching workspaces: 2' ([System.Windows.Automation.ControlType]::Text) 'Quick.ResultCount' } 'whitespace preserves all workspaces'
        Set-Field 'Quick.Search' '  context   SHIFT  '
        $null = Wait-For { Find-Control 'Matching workspaces: 1' ([System.Windows.Automation.ControlType]::Text) 'Quick.ResultCount' } 'independent description and name search terms'
        $null = Wait-For { $choice = Quick-Selection; if ($choice -and $choice.Current.AutomationId -ceq $firstSelectionId) { $choice } } 'cross-field search retains the correct result'
        Set-Field 'Quick.Search' 'SHIFT-no-owned-workspace-matches-this-8d7f'
        $null = Wait-For { Find-Control 'Matching workspaces: 0' ([System.Windows.Automation.ControlType]::Text) 'Quick.ResultCount' } 'empty compact search count'
        foreach ($actionId in @('Quick.Launch','Quick.Review')) {
            $null = Wait-For { $button = Find-Control '' ([System.Windows.Automation.ControlType]::Button) $actionId; if ($button -and -not $button.Current.IsEnabled) { $button } } "disabled empty compact action $actionId"
        }
        if ($null -ne (Quick-Selection)) { throw 'Empty compact search retained a selectable workspace.' }
        Capture 'quick-launcher-empty'
        Set-Field 'Quick.Search' 'context SHIFT'
        $null = Wait-For { Quick-Selection } 'compact review selection'
        Click '' 'Quick.Review'
        $null = Wait-For { (Find-Control '' ([System.Windows.Automation.ControlType]::Button) 'Workspace.Launch') -and
            (Find-Control 'Disposable launcher diagnostic context' ([System.Windows.Automation.ControlType]::Text)) } 'compact Review opens saved workspace detail'
        if ($null -ne (Find-Control 'Allow and launch' ([System.Windows.Automation.ControlType]::Button))) { throw 'Compact Review unexpectedly requested command consent.' }
        Click '' 'Nav.History'
        $historyAfterReview = Wait-For { History-Count } 'history count after compact review'
        if ($historyAfterReview -ne $historyBeforeReview -or
            (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash -cne $markerBeforeReview.Hash -or
            (Get-Item -LiteralPath $marker).LastWriteTimeUtc -ne $markerWriteBeforeReview) { throw 'Compact Review changed dispatch history or command proof.' }
        # Enter uses the existing consent flow. Explicit refusal must not rerun the already-approved command.
        Click '' 'Quick.Open'
        Set-Field 'Quick.Search' 'context SHIFT'
        $null = Wait-For { Quick-Selection } 'compact Enter selection'
        Quick-Key '{ENTER}'
        $null = Wait-For { Find-Control 'Allow and launch' ([System.Windows.Automation.ControlType]::Button) } 'compact Enter requests command consent'
        Click 'Cancel'
        $null = Wait-For { Find-Control 'Commands declined' ([System.Windows.Automation.ControlType]::Text) } 'compact keyboard command refusal'
        Click '' 'Nav.History'
        $null = Wait-For { $count = History-Count; if ($null -ne $count -and $count -eq ($historyBeforeReview + 1)) { $true } } 'one explicitly declined keyboard run recorded'
        if ((Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash -cne $markerBeforeReview.Hash -or
            (Get-Item -LiteralPath $marker).LastWriteTimeUtc -ne $markerWriteBeforeReview) { throw 'Keyboard Enter bypassed command consent.' }
        Click '' 'Nav.Workspaces'
        $null=Wait-For { Find-Control 'SHIFT  /  WORKSPACES' ([System.Windows.Automation.ControlType]::Text) } 'workspaces heading'
        Capture 'workspaces'
        Click '' 'Nav.Overview'
        $null=Wait-For { Find-Control 'SHIFT  /  OVERVIEW' ([System.Windows.Automation.ControlType]::Text) } 'overview heading'
        Capture 'overview'
        $process.CloseMainWindow() | Out-Null
        $null = Wait-For { -not [ShiftWindowProbe]::IsWindowVisible($process.MainWindowHandle) } 'close to tray'
        $second = Start-Process $exe -WorkingDirectory $app -PassThru
        try { if (-not $second.WaitForExit(10000)) { $second.Kill();throw 'Second instance did not exit.' } }
        finally { $second.Dispose() }
        $null = Wait-For { $process.Refresh();$process.MainWindowHandle -ne [IntPtr]::Zero -and [ShiftWindowProbe]::IsWindowVisible($process.MainWindowHandle) } 'instance activation'
        # Exercise graceful exit, then confirm the saved workspace/settings after restart.
        Click '' 'Nav.Settings'
        $tray = Wait-For {
            $scope=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $condition=New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Settings.MinimizeToTray')
            $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
        } 'tray setting'
        $toggle=$tray.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On){$toggle.Toggle()}
        # Save is asynchronous. The permanent Overview navigation button is not a
        # completion signal: closing against the previous setting legitimately hides
        # the app to tray. A fresh, enabled Save peer proves BuildShell has rebuilt
        # Settings after persistence; the fresh tray toggle must also confirm Off.
        $saveBefore=Wait-For { Find-Control 'Save' ([System.Windows.Automation.ControlType]::Button) } 'settings save button'
        $saveRuntimeBefore=$saveBefore.GetRuntimeId() -join ':'
        $trayRuntimeBefore=$tray.GetRuntimeId() -join ':'
        $saveBefore.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        $null=Wait-For {
            $saveAfter=Find-Control 'Save' ([System.Windows.Automation.ControlType]::Button)
            if($saveAfter -and $saveAfter.Current.IsEnabled -and ($saveAfter.GetRuntimeId() -join ':') -ne $saveRuntimeBefore){
                $scope=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                $condition=New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Settings.MinimizeToTray')
                $trayAfter=$scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
                if($trayAfter -and ($trayAfter.GetRuntimeId() -join ':') -ne $trayRuntimeBefore -and
                   $trayAfter.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::Off){$true}
            }
        } 'persisted close behavior in rebuilt settings'
        Write-Host 'Settings save acknowledged: fresh enabled Save peer and fresh tray toggle Off.'
        $process.Refresh()
        if(-not $process.CloseMainWindow()){throw 'Windows did not accept the graceful close request.'}
        if(-not $process.WaitForExit(15000)){
            $process.Refresh()
            Write-Host "Close timeout: handle=$($process.MainWindowHandle), visible=$([ShiftWindowProbe]::IsWindowVisible($process.MainWindowHandle)), responding=$($process.Responding)."
            throw 'Graceful application exit timed out.'
        }
        if($process.ExitCode -ne 0){throw "Graceful exit failed ($($process.ExitCode))."}
        $process.Dispose()
        $process=Start-Process $exe -WorkingDirectory $app -PassThru
    }
    $null=Wait-For { Find-Control 'Overview' ([System.Windows.Automation.ControlType]::Button) } 'persisted English language after restart'
    Click '' 'Quick.Open'
    Set-Field 'Quick.Search' 'SHIFT smoke'
    $null=Wait-For { Find-Control '↗  SHIFT smoke workspace' ([System.Windows.Automation.ControlType]::Text) } 'persisted workspace after restart'
    Click '' 'Quick.Close'
    Click '' 'Nav.Settings'
    $theme=Wait-For { Find-Control '' ([System.Windows.Automation.ControlType]::ComboBox) 'Field.Theme' } 'persisted theme'
    $selected=$theme.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if($selected.Count -ne 1 -or $selected[0].Current.Name -ne 'Midnight'){throw 'Theme did not survive restart.'}
    Capture 'settings-restored'
    if(-not(Test-Path (Join-Path $data 'dispatch-proof.txt'))){throw 'Command proof data did not survive restart.'}
    $process.CloseMainWindow() | Out-Null
    if(-not $process.WaitForExit(15000)){throw 'Restarted application did not exit gracefully.'}
    if($process.ExitCode -ne 0){throw "Restarted application exited with $($process.ExitCode)."}
    if (-not (Test-Path (Join-Path $data 'shift.db'))) { throw 'SQLite database missing.' }
    $errors=Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | Select-String -Pattern '\sERROR\s'
    if($errors){throw 'Application logged an unexpected error during UI smoke.'}
    $checks=@('workspace_persistence','english_persistence','midnight_theme_persistence','command_proof_persistence','graceful_exit')
    if(-not $ResumeOwnedData){$checks+=@('workspace_create','item_create','doctor_read_only','doctor_repeat','command_refusal','command_approval','real_dispatch','compact_search','compact_selection_pattern','compact_keyboard_navigation','compact_cross_field_search','compact_empty_actions','compact_review_without_dispatch','compact_keyboard_command_refusal','tray','second_instance_activation','restart')}
    if($DiagnosticsDirectory){
        @{schema_version=1;status='passed';environment='disposable-windows-ui-automation';mode=$label;resumed=[bool]$ResumeOwnedData;portable_smoke_archive=[bool]$ExtractedArchive;checks=$checks;manual_acceptance=$false;timestamp_utc=[DateTime]::UtcNow.ToString('o')} |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $DiagnosticsDirectory "$label-smoke.json")
    }
    Write-Host "UI smoke passed ($label): $($checks -join ', ')."
} catch {
    try { Capture 'failure' } catch { Write-Warning 'Screenshot capture failed.' }
    if ($null -ne $monitor -and $dumpDirectory) {
        $null=$monitor.WaitForExit(10000)
        Get-Content (Join-Path $dumpDirectory 'monitor.log') -ErrorAction SilentlyContinue | Write-Host
        foreach($dump in @(Get-ChildItem $dumpDirectory -Filter '*.dmp')) {
            if ($env:SHIFT_CRASH_PROBE) {
                $nativeLog=Join-Path $dumpDirectory 'native-stack.txt'
                $probe=Start-Process $env:SHIFT_CRASH_PROBE -ArgumentList @("`"$($dump.FullName)`"") -PassThru -RedirectStandardOutput $nativeLog
                if(-not $probe.WaitForExit(45000)){$probe.Kill();$probe.WaitForExit()}
                Get-Content $nativeLog -ErrorAction SilentlyContinue | Write-Host
                if($DiagnosticsDirectory){Copy-Item $nativeLog (Join-Path $DiagnosticsDirectory "$label-native-stack.txt")}
                $probe.Dispose()
            }
        }
    }
    # Native XAML failures can occur before managed application logging starts.
    # Read only events emitted during this disposable smoke, filtered to this app.
    try {
        $events = Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$startedAt} -ErrorAction Stop |
            Where-Object { $_.Message -match '(?i)SHIFT\.exe|SHIFT\.dll' } |
            Select-Object TimeCreated, Id, ProviderName, Message | Format-List | Out-String
        Write-Host $events
        if ($DiagnosticsDirectory) { $events | Set-Content (Join-Path $DiagnosticsDirectory "$label-windows-events.txt") }
    } catch { Write-Warning "No matching Windows event diagnostics: $($_.Exception.Message)" }
    Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Tail 40 }
    throw
} finally {
    if ($DiagnosticsDirectory -and (Test-Path (Join-Path $data 'logs'))) { Copy-Item (Join-Path $data 'logs') (Join-Path $DiagnosticsDirectory "$label-logs") -Recurse -Force }
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { $process.Kill();$process.WaitForExit() }
        $process.Dispose()
    }
    if ($null -ne $monitor) { if (-not $monitor.HasExited) { $monitor.Kill();$monitor.WaitForExit() };$monitor.Dispose() }
    if (-not $PreserveData -and (Test-Path $data)) { Remove-Item $data -Recurse -Force }
}
