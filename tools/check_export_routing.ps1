# ══════════════════════════════════════════════════════════════════════════
#  check_export_routing.ps1
#
#  Fails the build when a file gains a bare OutputLocationHelper
#  GetOutputDirectory / GetOutputPath / GetTimestampedPath call beyond its
#  count in tools/export_routing_baseline.txt.
#
#  WHY. Those calls do not know what they write, so every one returns the
#  project's MISC folder. On 2026-09-27 there were 257 of them in 153 files -
#  PDFs, IFCs, registers, BOQs, audits and round-trip workbooks all landing in
#  20_MISC, and nothing ever reaching an A_/M_/S_ discipline folder. 203 were
#  migrated to GetRoutedDirectory / GetRoutedPath / GetRoutedTimestampedPath
#  with an export-type key; the rest are listed in the baseline with a reason.
#  Without a gate the count only goes back up.
# ══════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$baseline = @{}
Get-Content (Join-Path $root 'tools/export_routing_baseline.txt') | ForEach-Object {
    if ($_ -match '^\s*#' -or $_.Trim() -eq '') { return }
    $parts = $_ -split "`t"
    $baseline[$parts[1].Trim()] = [int]$parts[0]
}

$pattern = [regex]'OutputLocationHelper\.(GetOutputDirectory|GetOutputPath|GetTimestampedPath)\s*\('
$counts = @{}
Get-ChildItem -Path (Join-Path $root 'StingTools') -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.Name -ne 'OutputLocationHelper.cs' } |
    ForEach-Object {
        $text = Get-Content $_.FullName -Raw
        # Comments that EXPLAIN a migration must not count as calls.
        $text = [regex]::Replace($text, '(?s)/\*.*?\*/', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
        $text = [regex]::Replace($text, '//[^\r\n]*', '')
        $n = $pattern.Matches($text).Count
        if ($n -gt 0) {
            $rel = $_.FullName.Substring($root.Length + 1) -replace '\\', '/'
            $counts[$rel] = $n
        }
    }

$over = @(); $under = @()
foreach ($k in $counts.Keys) {
    $allowed = if ($baseline.ContainsKey($k)) { $baseline[$k] } else { 0 }
    if ($counts[$k] -gt $allowed) { $over += "  $k : $($counts[$k]) (allowed $allowed)" }
    elseif ($counts[$k] -lt $allowed) { $under += "  $k : $($counts[$k]) (baseline $allowed - lower it)" }
}
foreach ($k in $baseline.Keys) {
    if (-not $counts.ContainsKey($k)) { $under += "  $k : 0 (baseline $($baseline[$k]) - remove the line)" }
}

$total = ($counts.Values | Measure-Object -Sum).Sum
Write-Host "Bare MISC-routed export calls: $total across $($counts.Count) file(s)"
if ($under.Count -gt 0) { Write-Host "Baseline can be ratcheted down:"; $under | ForEach-Object { Write-Host $_ } }
if ($over.Count -gt 0) {
    Write-Host "FAIL: new bare OutputLocationHelper export calls. Use GetRoutedDirectory(doc, key[, discipline]) /"
    Write-Host "GetRoutedPath / GetRoutedTimestampedPath so the file lands in its project folder:"
    $over | ForEach-Object { Write-Host $_ }
    exit 1
}
Write-Host "PASS: export routing within baseline."
exit 0
