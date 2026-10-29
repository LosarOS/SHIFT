[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+-rc\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSourceCommit
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Existing release smoke requires a disposable Windows CI runner.' }
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo 'artifacts'
$tag = "v$Version"
$files = @("SHIFT-$Version-win-x64-portable.zip", "SHIFT-$Version-win-x64-setup.exe")
$download = Join-Path $out ('original-release-' + [guid]::NewGuid().ToString('N'))
New-Item $download -ItemType Directory -Force | Out-Null
Push-Location $repo
try {
    $tagged = gh api "repos/$env:GITHUB_REPOSITORY/commits/$tag" --jq '.sha'
    if ($LASTEXITCODE -ne 0 -or $tagged -cne $ExpectedSourceCommit) { throw 'Original tag/source mismatch.' }
    git fetch --no-tags origin $ExpectedSourceCommit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot fetch original source for comparison.' }
    git diff --exit-code $ExpectedSourceCommit HEAD -- src Directory.Build.props Directory.Build.targets global.json NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Application/build inputs changed; existing binaries cannot validate the changed application.' }
    gh release download $tag --dir $download --pattern $files[0] --pattern $files[1] --pattern 'SHA256SUMS.txt' --pattern 'WINDOWS-VALIDATION.json'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download original release bytes.' }
    $original = Get-Content (Join-Path $download 'WINDOWS-VALIDATION.json') -Raw | ConvertFrom-Json
    if ($original.source_commit -cne $ExpectedSourceCommit -or $original.version -cne $Version) { throw 'Original manifest source/version differs.' }
    $lines = @([IO.File]::ReadAllLines((Join-Path $download 'SHA256SUMS.txt')))
    if ($lines.Count -ne 2) { throw 'Unexpected original checksum count.' }
    $observed = @{}
    foreach ($line in $lines) {
        if ($line -cnotmatch '^([0-9a-f]{64})  ([A-Za-z0-9.\-]+)$') { throw 'Invalid checksum entry.' }
        $digest = $Matches[1]; $name = $Matches[2]
        if ($name -notin $files -or $observed.ContainsKey($name)) { throw 'Unexpected or duplicate original binary.' }
        $path = Join-Path $download $name
        if ((Get-Item $path).Length -le 0 -or (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $digest) { throw 'Original downloaded binary hash mismatch.' }
        $observed[$name] = $digest
    }
    $extracted = Join-Path $download 'extracted'
    Expand-Archive -LiteralPath (Join-Path $download $files[0]) -DestinationPath $extracted
    $app = Join-Path $extracted "SHIFT-$Version-win-x64-portable"
    if (-not (Test-Path (Join-Path $app 'SHIFT.exe')) -or -not (Test-Path (Join-Path $app 'portable.flag'))) { throw 'Original portable layout is incomplete.' }
    & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Windows.ps1') -AppDirectory $app -ExtractedArchive -DiagnosticsDirectory (Join-Path $out 'diagnostics')
    if ($LASTEXITCODE -ne 0) { throw 'Original portable binary did not pass corrected UI smoke.' }
    $studioChecks = @{}
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $studioPath = Join-Path $out ("diagnostics/studio-original-$attempt")
        & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Smoke-Studio.ps1') -AppDirectory $app -DiagnosticsDirectory $studioPath
        if ($LASTEXITCODE -ne 0) { throw "Original Studio/Russian feature revalidation failed on attempt $attempt." }
        $entry = Get-Content (Join-Path $studioPath 'studio-smoke.json') -Raw | ConvertFrom-Json
        if ($entry.status -ne 'passed' -or $entry.wallpaper_verified -ne $true -or $entry.manual_acceptance -ne $false) { throw 'Original Studio evidence is incomplete.' }
        foreach ($gate in @('russian_live_language_without_recreation','russian_quick_setup_order_save_cancel','starter_preset_cancel_atomic_save_and_apply','editor_preserves_boundaries_custom_style_and_missing_monitor','disabled_profile_stays_disabled_after_restart','recovery_gather_persists_geometry')) {
            if ($gate -notin $entry.checks) { throw "Missing original Studio gate: $gate" }
        }
        $studioChecks["attempt_$attempt"] = $entry
    }
    $setup = Join-Path $out $files[1]
    if (Test-Path $setup) { throw 'Refusing to replace an existing setup artifact.' }
    Copy-Item (Join-Path $download $files[1]) $setup
    & (Join-Path $PSScriptRoot 'Smoke-Installer.ps1') -Version $Version
    $nativeChecks = @('diagnostics/portable-smoke.json', 'diagnostics/installed-smoke.json', 'diagnostics/installer-smoke.json', 'diagnostics-reinstall/installed-smoke.json')
    $evidence = @{}
    foreach ($name in $nativeChecks) {
        $entry = Get-Content (Join-Path $out $name) -Raw | ConvertFrom-Json
        if ($entry.status -ne 'passed' -or $entry.manual_acceptance -ne $false) { throw 'Missing corrected native smoke evidence.' }
        $evidence[$name] = $entry
    }
    if ($evidence['diagnostics/portable-smoke.json'].portable_smoke_archive -ne $true) { throw 'Corrected smoke did not use extracted original ZIP.' }
    $report = [ordered]@{
        schema_version = 1; status = 'passed'; version = $Version
        binary_source_commit = $ExpectedSourceCommit; verification_scripts_commit = $env:GITHUB_SHA
        application_source_unchanged = $true; original_assets_unchanged = $true
        original_binary_sha256 = $observed
        environment = 'GitHub Actions disposable Windows x64 runner'
        workflow_run = "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
        diagnosis = 'Original published binaries were downloaded and verified against their checksums and source. Two complete Studio passes and the listed portable/installed preservation checks ran with the recorded verification-script commit, without rebuilding or replacing application assets.'
        checks = $evidence; studio_repeated_checks = $studioChecks; original_core_tests = $original.core_tests; manual_acceptance = $false
    }
    $proof = Join-Path $out "WINDOWS-REVALIDATION-$env:GITHUB_RUN_ID.json"
    $report | ConvertTo-Json -Depth 12 | Set-Content $proof -Encoding utf8
    gh release upload $tag $proof
    if ($LASTEXITCODE -ne 0) { throw 'Cannot attach additive revalidation evidence; original release remains held.' }
    Write-Host "Original release binaries revalidated. No tag or binary asset was replaced. Proof: $proof"
} finally { Pop-Location }
