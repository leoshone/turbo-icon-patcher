# patch_turbo_icon.ps1 - portable edition (no Python, no installs).
# Orchestrates the three icon sources; binary/icon work is done by
# scripts/IconPatcher.cs, compiled at runtime via Add-Type (inbox csc.exe).
# Parity reference: scripts/patch_icon.py (leoshone/turbo-icon-patcher).
param(
    [string]$Root = "",
    [string]$Icon = "",
    [string]$RefExe = "",
    [switch]$AllProfiles,
    [switch]$Apply,
    [switch]$Verify
)
$ErrorActionPreference = 'Stop'
# Tool root = parent of this script's folder (script lives in <tool>\scripts\).
$Tool = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$CsPath = Join-Path $Tool 'scripts\IconPatcher.cs'

# ---- resolve turbo root: parent folder of this tool ----
if (-not $Root) { $Root = Split-Path -Parent $Tool }
$Exe = Join-Path $Root 'turbo.exe'
if (-not (Test-Path $Exe)) { Write-Host "[ERROR] turbo.exe not found in $Root"; exit 1 }
if (-not $Icon) { $Icon = Join-Path $Tool 'assets\edited_256.png' }
if (-not (Test-Path $Icon)) { Write-Host "[ERROR] icon master not found: $Icon"; exit 1 }

# ---- compile engine (inbox csc.exe via Add-Type, ~1s) ----
Add-Type -TypeDefinition (Get-Content $CsPath -Raw) `
    -ReferencedAssemblies System.Drawing `
    -Language CSharp | Out-Null
$T = [TurboIcon.IconPatcher]
Write-Host "[OK] engine compiled (inbox csc.exe)"

# ---- verify mode: live window icon score ----
if ($Verify) {
    $snap = Join-Path $Tool 'assets\_live_snapshot.png'
    $score = $T::LiveIconScore('Turbo', $snap)
    if ($score -lt 0) { Write-Host 'no visible window matching "Turbo"'; exit 2 }
    $verdict = 'NEW'; if ($score -ge 8) { $verdict = 'OLD' }
    Write-Host ("live icon yellow-bar score={0} -> {1} icon (snapshot: {2})" -f $score, $verdict, $snap)
    exit 0
}

$master = [System.Drawing.Image]::FromFile($Icon)
try { if (-not ($master.Width -eq 256 -and $master.Height -eq 256)) { throw 'master must be 256x256' } } finally { $master.Dispose() }
$MasterPng = [System.IO.File]::ReadAllBytes($Icon)

# ---- collect old frames (patching baseline) ----
# Baseline comes from the CURRENT turbo.exe: when it still carries the official
# icon this is exactly the chrome.dll baseline; after a fresh upgrade both exe
# and dll are official again, so this holds for the normal upgrade scenario.
$mainIds = $T::GetMainFrameIds($Exe)
if (-not $mainIds -or $mainIds.Count -eq 0) {
    Write-Host '[WARN] IDR_MAINFRAME group not found, fallback to ids 1..8'
    $mainIds = @(1..8)
}
$refExePath = if ($RefExe) { $RefExe } else { $Exe }
$oldFramesDict = $T::CollectFrames($refExePath, $mainIds)
# keep ascending id order for readable output
$pair = @($(foreach ($kv in $oldFramesDict.GetEnumerator()) {
    [pscustomobject]@{ id = $kv.Key; old = $kv.Value; size = $T::PngSize($kv.Value) }
}) | Sort-Object id)
$ids = @($pair | ForEach-Object { [int]$_.id })
$oldFrames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($p in $pair) { $oldFrames.Add([byte[]]$p.old) }
$sizes = New-Object 'System.Collections.Generic.List[int[]]'
foreach ($p in $pair) { $sizes.Add([int[]]$p.size) }

# ---- status line: is exe already new? ----
$bigId = $null
for ($i = 0; $i -lt $ids.Count; $i++) { if ($sizes[$i][0] -eq 256 -and $sizes[$i][1] -eq 256) { $bigId = $ids[$i]; break } }
$exeIsNew = $false
if ($null -ne $bigId) {
    $curBig = $T::GetFrame($Exe, $bigId)
    if ($curBig) {
        $newBig = if ($sizes[$ids.IndexOf($bigId)][0] -eq 256) { $MasterPng } else { $null }
        if ($newBig) {
            $sha = New-Object System.Security.Cryptography.MD5CryptoServiceProvider
            $exeIsNew = [Convert]::ToBase64String($sha.ComputeHash($curBig)) -eq [Convert]::ToBase64String($sha.ComputeHash($newBig))
        }
    }
}
$baseLabel = if ($exeIsNew) { 'NEW patched' } else { 'OLD/unmatched' }
Write-Host ('[status] exe 256px(id=' + $bigId + '): ' + $baseLabel + '; chrome.dll baseline: ' + $refExePath)

# ---- [1/3] turbo.exe RT_ICON (IDR_MAINFRAME only) ----
Write-Host ('[1/3] turbo.exe RT_ICON (IDR_MAINFRAME ' + $ids.Count + ' frames)')
if ($Apply) {
    $newFrames = New-Object System.Collections.Generic.List[byte[]]
    for ($i = 0; $i -lt $ids.Count; $i++) {
        $s = $sizes[$i]
        $newFrames.Add($(if ($s[0] -eq 256 -and $s[1] -eq 256) { $MasterPng } else { $T::ResizePng($MasterPng, $s[0], $s[1]) }))
    }
    $T::UpdateExeIcons($Exe, [int[]]$ids, $newFrames.ToArray())
} else {
    Write-Host ('  [exe] dry-run: would update ' + $ids.Count + ' RT_ICON frames via UpdateResource')
}

# ---- [2/3] Google Profile.ico ----
Write-Host '[2/3] Google Profile.ico'
$ud = Join-Path $Root 'User Data'
$profiles = @('Default')
if ($AllProfiles -and (Test-Path $ud)) {
    $profiles = @(Get-ChildItem -Directory $ud | Where-Object { Test-Path (Join-Path $_.FullName 'Google Profile.ico') } | ForEach-Object { $_.Name })
    if (-not $profiles -or $profiles.Count -eq 0) { $profiles = @('Default') }
}
foreach ($p in $profiles) {
    $ico = Join-Path $ud (Join-Path $p 'Google Profile.ico')
    if (Test-Path $ico) { $T::PatchIco($ico, $MasterPng, [bool]$Apply) }
}

# ---- [3/3] chrome.dll embedded PNG ----
Write-Host '[3/3] chrome.dll embedded PNG (live window icon source)'
$verDirs = @(Get-ChildItem -Directory $Root | Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } | ForEach-Object { $_.Name })
foreach ($vd in $verDirs) {
    $dll = Join-Path (Join-Path $Root $vd) 'chrome.dll'
    if (Test-Path $dll) { $T::PatchChromeDll($dll, $MasterPng, [int[]]$ids, $oldFrames.ToArray(), $sizes, [bool]$Apply) }
}

Write-Host ''
Write-Host 'Done hint: restart browser to take effect; if taskbar is stubborn: ie4uinit -Show + restart explorer.'
exit 0
