# Run with Windows PowerShell 5.1: uses built-in UI Automation assemblies.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$app = (Resolve-Path $AppDirectory).Path
$exe = Join-Path $app 'SHIFT.exe'
$data = Join-Path $app 'data'
if (-not (Test-Path (Join-Path $app 'portable.flag'))) { throw 'Smoke requires a disposable portable build.' }
if (Test-Path $data) { throw 'Refusing to smoke-test a folder with existing user data.' }
$process = $null
function Wait-For([scriptblock]$Probe, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "SHIFT exited ($($process.ExitCode)) while waiting for $Description." }
        $result = & $Probe
        if ($null -ne $result -and $result -ne $false) { return $result }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $Description."
}
function Find-Control([string]$Name, [System.Windows.Automation.ControlType]$Type) {
    $process.Refresh()
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { return $null }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition -ArgumentList @([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $Type)
    $condition = New-Object System.Windows.Automation.AndCondition -ArgumentList @($nameCondition, $typeCondition)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Button($Button) {
    $pattern = $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}
try {
    $process = Start-Process $exe -WorkingDirectory $app -PassThru
    $empty = Wait-For { Find-Control 'Начать с чистого листа' ([System.Windows.Automation.ControlType]::Button) } 'onboarding'
    Invoke-Button $empty
    $create = Wait-For { Find-Control '+  Новое пространство' ([System.Windows.Automation.ControlType]::Button) } 'new workspace action'
    Invoke-Button $create
    $name = Wait-For { Find-Control 'Название' ([System.Windows.Automation.ControlType]::Edit) } 'workspace name editor'
    $name.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('SHIFT smoke workspace')
    $save = Wait-For { Find-Control 'Сохранить' ([System.Windows.Automation.ControlType]::Button) } 'workspace save'
    Invoke-Button $save
    $null = Wait-For { Find-Control 'SHIFT smoke workspace' ([System.Windows.Automation.ControlType]::Text) } 'saved workspace detail'
    $settings = Wait-For { Find-Control 'Настройки' ([System.Windows.Automation.ControlType]::Button) } 'settings navigation'
    Invoke-Button $settings
    $saveSettings = Wait-For { Find-Control 'Сохранить' ([System.Windows.Automation.ControlType]::Button) } 'settings save'
    Invoke-Button $saveSettings
    $null = Wait-For { Find-Control 'Сохранено' ([System.Windows.Automation.ControlType]::Text) } 'shell rebuild after settings save'
    if (-not (Test-Path (Join-Path $data 'shift.db'))) { throw 'SQLite database missing after save.' }
    Write-Host 'WinUI smoke passed: startup, onboarding, create dialog, save, workspace detail, settings rebuild, database.'
} catch {
    Get-ChildItem (Join-Path $data 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Tail 30 }
    throw
} finally {
    if ($null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    # This script already refused existing data; remove only the newly-created smoke dataset.
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
}
