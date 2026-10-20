[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-rc\.\d+)?$')][string]$Version = '1.0.0-rc.1',
    [string]$IsccPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Release packaging requires Windows x64 and PowerShell 7.' }
if (-not (Test-Path $IsccPath)) { throw 'Install Inno Setup 6 or pass -IsccPath.' }
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo 'artifacts'
$publish = Join-Path $out 'publish'
$portable = Join-Path $out "SHIFT-$Version-win-x64-portable"
# Work only inside the repository artifacts directory.
New-Item $out -ItemType Directory -Force | Out-Null
foreach ($target in @($publish, $portable)) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
}
Push-Location $repo
try {
    dotnet restore SHIFT.sln --locked-mode --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
    dotnet test tests/Shift.Tests/Shift.Tests.csproj -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    $effective=dotnet msbuild src/Shift.App/Shift.App.csproj -getProperty:WindowsAppSDKSelfContained,WindowsAppSdkBootstrapInitialize | Out-String | ConvertFrom-Json
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect app runtime initialization.'}
    if($effective.Properties.WindowsAppSDKSelfContained -eq 'true' -and $effective.Properties.WindowsAppSdkBootstrapInitialize -eq 'true'){
        throw 'Self-contained apps must not bootstrap a separately installed Windows App SDK runtime.'
    }
    dotnet publish src/Shift.App/Shift.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true "-p:Version=$Version" -o $publish --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    Copy-Item LICENSE (Join-Path $publish 'SHIFT-LICENSE.txt')
    Copy-Item THIRD-PARTY-NOTICES.md $publish
    $licenses = Join-Path $publish 'licenses'
    New-Item $licenses -ItemType Directory -Force | Out-Null
    Copy-Item third-party/* $licenses
    $dotnetRoot = Split-Path (Get-Command dotnet).Source -Parent
    foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        $source = Join-Path $dotnetRoot $name
        if (Test-Path $source) { Copy-Item $source (Join-Path $licenses "dotnet-$name") }
    }
    $assets = Get-Content src/Shift.App/obj/project.assets.json -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $package = Join-Path $folder $library.Value.path
            if (-not (Test-Path $package)) { continue }
            $destination = Join-Path $licenses ($library.Name -replace '/', '-')
            New-Item $destination -ItemType Directory -Force | Out-Null
            Get-ChildItem $package -File | Where-Object { $_.Name -match '(?i)license|notice|copying|\.nuspec$' } | Copy-Item -Destination $destination
            break
        }
    }
    Copy-Item $publish $portable -Recurse
    New-Item (Join-Path $portable 'portable.flag') -ItemType File -Force | Out-Null
    Copy-Item docs/INSTALLATION.md (Join-Path $portable 'INSTALLATION.md')
    & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Windows.ps1') -AppDirectory $portable -DiagnosticsDirectory (Join-Path $out 'diagnostics')
    if ($LASTEXITCODE -ne 0) { throw 'Windows UI smoke failed.' }
    $zip = Join-Path $out "SHIFT-$Version-win-x64-portable.zip"
    Compress-Archive -Path $portable -DestinationPath $zip -Force
    $numericVersion=($Version -split '-',2)[0]+'.0'
    & $IsccPath "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DSourceDir=$publish" "/DOutputDir=$out" (Join-Path $PSScriptRoot 'SHIFT.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $exe = Join-Path $out "SHIFT-$Version-win-x64-setup.exe"
    if (-not (Test-Path $exe)) { throw 'Installer artifact missing.' }
    $hashes = @($zip, $exe) | ForEach-Object {
        $hash = Get-FileHash $_ -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $_ -Leaf)
    }
    [IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), [string[]]$hashes)
    Write-Host "Built unsigned release artifacts in $out. Complete docs/QA.md before publishing."
} finally { Pop-Location }
