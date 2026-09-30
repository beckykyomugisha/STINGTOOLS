# Collects what is needed to tell why STING did or did not load, into one zip on
# the Desktop, and prints a short diagnosis. Send that zip back with screenshots.
#
# Keep this file plain ASCII: Windows PowerShell 5.1 reads a .ps1 without a BOM
# as ANSI, and one typographic dash inside a string is a parser error there.
$ErrorActionPreference = 'SilentlyContinue'
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$out   = Join-Path ([Environment]::GetFolderPath('Desktop')) "STING_logs_$stamp"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$report = New-Object System.Collections.Generic.List[string]
function Say([string]$t, [string]$c = 'Gray') { Write-Host $t -ForegroundColor $c; $report.Add($t) }

Say "STING diagnostics  $(Get-Date -Format 'yyyy-MM-dd HH:mm')  user=$env:USERNAME  pc=$env:COMPUTERNAME" Cyan
Say "Package folder: $here"
Say "Revit running now: $([bool](Get-Process -Name Revit))"

# 1. Every manifest Revit will read for STING, and the DLL each one loads.
$loadedDirs = @()
foreach ($ver in '2025','2026','2027') {
    $installed = Test-Path "C:\Program Files\Autodesk\Revit $ver\RevitAPI.dll"
    Say ""
    Say "Revit ${ver}: installed=$installed" Cyan
    foreach ($dir in @((Join-Path $env:APPDATA "Autodesk\Revit\Addins\$ver"), "C:\ProgramData\Autodesk\Revit\Addins\$ver")) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -like '*.addin*' } | ForEach-Object {
            $t = Get-Content -LiteralPath $_.FullName -Raw
            if (-not ($t -match 'A1B2C3D4-5678-9ABC-DEF0-123456789ABC' -or $t -match 'StingTools')) { return }
            Copy-Item -LiteralPath $_.FullName (Join-Path $out ("Revit{0}_{1}" -f $ver, $_.Name))
            $active = $_.Name -like '*.addin'
            $asm = $null
            try { $asm = ([xml]$t).RevitAddIns.AddIn.Assembly } catch { Say "  $($_.Exception.Message)" Red }
            if ($asm -and -not [IO.Path]::IsPathRooted($asm)) { $asm = Join-Path $dir $asm }
            $exists = $asm -and (Test-Path -LiteralPath $asm)
            $blocked = $exists -and [bool](Get-Item -LiteralPath $asm -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue)
            $colour = if (-not $active) { 'DarkGray' } elseif ($exists -and -not $blocked) { 'Green' } else { 'Red' }
            Say ("  {0} {1}" -f ($(if ($active) { '[ACTIVE]  ' } else { '[disabled]' })), $_.FullName) $colour
            Say ("             loads: {0}" -f $asm) $colour
            if ($active) {
                if (-not $asm) { Say "             PROBLEM: manifest could not be read (invalid XML?)" Red }
                elseif (-not $exists) { Say "             PROBLEM: that DLL does not exist" Red }
                elseif ($blocked) { Say "             PROBLEM: DLL is marked as downloaded (blocked). Run install.bat again." Red }
                else {
                    $f = Get-Item -LiteralPath $asm
                    Say ("             DLL built {0:yyyy-MM-dd HH:mm}, {1:N0} bytes" -f $f.LastWriteTime, $f.Length)
                    $loadedDirs += $f.DirectoryName
                }
            }
        }
    }
}

# 1b. Autodesk app bundles: every Revit version loads *.addin found inside these.
Say ""
Say "App bundles (ApplicationPlugins):" Cyan
foreach ($root in @((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'), 'C:\ProgramData\Autodesk\ApplicationPlugins', 'C:\Program Files\Autodesk\ApplicationPlugins')) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Name -like '*.addin*' } | ForEach-Object {
        $t = Get-Content -LiteralPath $_.FullName -Raw
        if ($t -match 'A1B2C3D4-5678-9ABC-DEF0-123456789ABC' -or $t -match 'StingTools') {
            Copy-Item -LiteralPath $_.FullName (Join-Path $out ('bundle_' + $_.Name))
            $colour = if ($_.Name -like '*.addin') { 'Red' } else { 'DarkGray' }
            Say "  $($_.FullName)" $colour
        }
    }
}

# 1c. Every StingTools.dll in the usual places, newest first. An old one that a
#     manifest above points at is the build Revit is actually running.
Say ""
Say "StingTools.dll copies on this PC:" Cyan
$roots = @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:USERPROFILE 'Downloads'),
           [Environment]::GetFolderPath('MyDocuments'), $env:APPDATA, $env:LOCALAPPDATA,
           'C:\ProgramData', 'C:\Program Files\Autodesk', 'C:\STINGTOOLS') | Select-Object -Unique
$copies = foreach ($r in $roots) {
    if (Test-Path -LiteralPath $r) { Get-ChildItem -LiteralPath $r -Recurse -Filter 'StingTools.dll' -File -ErrorAction SilentlyContinue }
}
$copies | Sort-Object LastWriteTime -Descending | Select-Object -First 25 | ForEach-Object {
    Say ("  {0:yyyy-MM-dd HH:mm}  {1}" -f $_.LastWriteTime, $_.FullName)
}
if (-not $copies) { Say "  none found in the usual places" }

# 2. STING's own logs (date-stamped: StingTools_yyyyMMdd.log) beside each DLL.
Say ""
$dirs = @($loadedDirs) + @(Join-Path $here 'CompiledPlugin') | Select-Object -Unique
$logs = foreach ($d in $dirs) { Get-ChildItem -LiteralPath $d -Filter 'StingTools*.log' -File }
if ($logs) {
    $logs | Sort-Object LastWriteTime -Descending | Select-Object -First 3 | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName $out
        Say ("STING log {0}  (last written {1:yyyy-MM-dd HH:mm})" -f $_.FullName, $_.LastWriteTime) Green
    }
} else {
    Say "No STING log beside any registered DLL: Revit has never started this STING." Yellow
}

# 3. Newest Revit journals, plus the lines in them that mention STING or add-ins.
foreach ($ver in '2025','2026','2027') {
    $jdir = Join-Path $env:LOCALAPPDATA "Autodesk\Revit\Autodesk Revit $ver\Journals"
    if (-not (Test-Path $jdir)) { continue }
    Get-ChildItem $jdir -Filter 'journal*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 2 | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $out ("Revit{0}_{1}" -f $ver, $_.Name))
        Select-String -LiteralPath $_.FullName -Pattern 'StingTools|STING Tools|A1B2C3D4|addin|add-in' |
            Select-Object -Last 15 | ForEach-Object { $report.Add("  journal ${ver}: " + $_.Line.Trim()) }
    }
    Say "Copied newest Revit $ver journals" Green
}

$report | Set-Content -LiteralPath (Join-Path $out 'diagnosis.txt') -Encoding UTF8
$zip = "$out.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Remove-Item $out -Recurse -Force
Write-Host ""
Write-Host "Logs zipped to:" -ForegroundColor Cyan
Write-Host "  $zip"
Write-Host "Send that file back, plus a screenshot of this window."
