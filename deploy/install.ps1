# STING Tools installer — writes a per-user .addin manifest for every
# installed Revit version, pointing at the CompiledPlugin folder that
# ships next to this script. No admin rights required.
#
# Shared content library (optional): the folder the team's tag families and
# symbols live in, usually a network share such as \\server\STING\ContentLibrary.
# Taken from -ContentLibrary, else from the first line of content_library.txt
# next to this script, and written to %APPDATA%\STING\sting_content.json
# ("content_root"). The plugin reads <that folder>\Tags before its own copy and
# falls back to its own copy whenever the share cannot be reached.
#
# Licence (optional): -Licence <path>, else the first *.lic next to this script,
# is copied to C:\ProgramData\Planscape\StingTools\StingTools.lic. A portable
# licence (issued with --any-machine) activates every PC it is installed on.
# Only relevant to builds with LicenseGate.Enforced = true.
#
# Older STING manifests (other file names, machine-wide copies) are disabled so
# Revit cannot keep loading a previous build.
param([string]$ContentLibrary, [string]$Licence)
$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll  = Join-Path $here 'CompiledPlugin\StingTools.dll'

Write-Host "STING Tools installer" -ForegroundColor Cyan
Write-Host "---------------------"

if (-not (Test-Path $dll)) {
    Write-Host "ERROR: CompiledPlugin\StingTools.dll was not found next to this script." -ForegroundColor Red
    Write-Host "Make sure you extracted the WHOLE zip and kept the folder structure intact."
    exit 1
}

$template = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>STING Tools</Name>
    <Assembly>__DLL__</Assembly>
    <AddInId>A1B2C3D4-5678-9ABC-DEF0-123456789ABC</AddInId>
    <FullClassName>StingTools.Core.StingToolsApp</FullClassName>
    <VendorId>Planscape</VendorId>
    <VendorDescription>Planscape - ISO 19650 BIM Automation</VendorDescription>
    <UseRevitContext>false</UseRevitContext>
  </AddIn>
</RevitAddIns>
"@
$addin = $template.Replace('__DLL__', $dll)

# Revit holds the old DLL and re-reads manifests only at startup.
if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Host "ERROR: Revit is running. Close every Revit window, then run install.bat again." -ForegroundColor Red
    exit 1
}

# A zip downloaded from the internet marks every file as blocked; clear that.
Get-ChildItem -LiteralPath $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# Any OTHER manifest that loads STING (an older install, a different file name, a
# machine-wide copy) wins or collides with ours, and Revit then runs that old DLL -
# which is how an old build keeps showing its licence window after an update.
function Find-StingManifests([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    Get-ChildItem -LiteralPath $dir -Filter '*.addin' -File -ErrorAction SilentlyContinue | Where-Object {
        $t = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
        $t -and ($t -match 'A1B2C3D4-5678-9ABC-DEF0-123456789ABC' -or $t -match 'StingTools\.Core\.StingToolsApp')
    }
}

$blocked = @()
$installed = 0
foreach ($ver in '2025','2026','2027') {
    $revitApi = "C:\Program Files\Autodesk\Revit $ver\RevitAPI.dll"
    if (Test-Path $revitApi) {
        $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$ver"
        New-Item -ItemType Directory -Force -Path $addinsDir | Out-Null
        $ours = Join-Path $addinsDir 'StingTools.addin'

        foreach ($m in (Find-StingManifests $addinsDir)) {
            if ($m.FullName -ieq $ours) { continue }
            $old = '?'
            try { $old = ([xml](Get-Content -LiteralPath $m.FullName -Raw)).RevitAddIns.AddIn.Assembly } catch { }
            Rename-Item -LiteralPath $m.FullName -NewName ($m.Name + '.disabled') -Force
            Write-Host "Revit ${ver}: disabled old manifest $($m.Name) (it loaded $old)" -ForegroundColor Yellow
        }
        foreach ($m in (Find-StingManifests "C:\ProgramData\Autodesk\Revit\Addins\$ver")) {
            try {
                Remove-Item -LiteralPath $m.FullName -Force -ErrorAction Stop
                Write-Host "Revit ${ver}: removed machine-wide manifest $($m.FullName)" -ForegroundColor Yellow
            } catch {
                Write-Host "Revit ${ver}: could NOT remove $($m.FullName) - it needs administrator rights." -ForegroundColor Red
                $blocked += $m.FullName
            }
        }

        Set-Content -LiteralPath $ours -Value $addin -Encoding UTF8
        Write-Host ("Installed for Revit {0}  ->  {1}" -f $ver, $ours) -ForegroundColor Green
        $installed++
    }
}

if ($blocked.Count -gt 0) {
    Write-Host ""
    Write-Host "An older STING install is still registered for all users, so Revit would keep loading it:" -ForegroundColor Red
    $blocked | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "Right-click install.bat > 'Run as administrator' to remove it, or delete those files by hand." -ForegroundColor Red
}

if ($installed -eq 0) {
    Write-Host "No Autodesk Revit 2025 / 2026 / 2027 install was detected on this PC." -ForegroundColor Yellow
    Write-Host "Install Revit first, then run this installer again."
    exit 1
}

if (-not $ContentLibrary) {
    $cfgFile = Join-Path $here 'content_library.txt'
    if (Test-Path $cfgFile) {
        $ContentLibrary = (Get-Content -LiteralPath $cfgFile |
            Where-Object { $_.Trim() -and -not $_.Trim().StartsWith('#') } |
            Select-Object -First 1)
        if ($ContentLibrary) { $ContentLibrary = $ContentLibrary.Trim() }
    }
}
if ($ContentLibrary) {
    $stingDir = Join-Path $env:APPDATA 'STING'
    $jsonPath = Join-Path $stingDir 'sting_content.json'
    New-Item -ItemType Directory -Force -Path $stingDir | Out-Null
    $cfg = [ordered]@{}
    if (Test-Path $jsonPath) {
        try {
            $old = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
            foreach ($p in $old.PSObject.Properties) { $cfg[$p.Name] = $p.Value }
            if ($old.content_root -and $old.content_root -ne $ContentLibrary) {
                Write-Host "Shared library changed from $($old.content_root)" -ForegroundColor Yellow
            }
        } catch { Write-Host "Replacing unreadable $jsonPath" -ForegroundColor Yellow }
    }
    $cfg['content_root'] = $ContentLibrary
    ($cfg | ConvertTo-Json) | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    Write-Host "Shared content library: $ContentLibrary  (saved in $jsonPath)" -ForegroundColor Green
    if (Test-Path -LiteralPath (Join-Path $ContentLibrary 'Tags')) {
        $n = (Get-ChildItem -LiteralPath (Join-Path $ContentLibrary 'Tags') -Filter 'STING - *.rfa' -File | Measure-Object).Count
        Write-Host "  Tags folder reachable: $n tag families." -ForegroundColor Green
    } else {
        Write-Host "  $ContentLibrary\Tags is not reachable from this PC right now." -ForegroundColor Yellow
        Write-Host "  STING will use the tag families shipped with the plugin until it is." -ForegroundColor Yellow
    }
} else {
    Write-Host "No shared content library set; STING uses the tag families shipped with the plugin."
}

if (-not $Licence) {
    $Licence = Get-ChildItem -LiteralPath $here -Filter '*.lic' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
}
if ($Licence) {
    if (Test-Path -LiteralPath $Licence) {
        $licDir = 'C:\ProgramData\Planscape\StingTools'
        try {
            New-Item -ItemType Directory -Force -Path $licDir | Out-Null
            Copy-Item -LiteralPath $Licence -Destination (Join-Path $licDir 'StingTools.lic') -Force
            Write-Host "Licence installed: $licDir\StingTools.lic  (STING > Activate shows its expiry)" -ForegroundColor Green
        } catch {
            Write-Host "Could not install the licence: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "Paste it in Revit instead: STING Tools > Activate STING." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Licence file not found: $Licence" -ForegroundColor Yellow
    }
} else {
    Write-Host "No licence file supplied. None is needed: this build has the licence check switched off."
}

Write-Host ""
Write-Host "DONE. Fully close Revit if it is open, then reopen it." -ForegroundColor Cyan
Write-Host "STING dockable panels appear on the right; a 'STING Tools' ribbon tab is also added."
Write-Host "Plugin location: $here\CompiledPlugin   (do not move this folder after installing)"
