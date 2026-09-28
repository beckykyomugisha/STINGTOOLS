# STING Tools - show this PC's licence machine code.
#
# Prints the same code the "Activate STING" dialog shows inside Revit (the
# MachineGuid-based "Stable" code). Send it to Planscape to receive your licence.
# It is also copied to the clipboard and saved as MachineCode.txt on your Desktop.
param([string]$MachineGuid)

function Get-StingMachineCode([string]$guid) {
    $n = if ($guid) { $guid.Trim().ToUpperInvariant() } else { "" }
    if ($n -eq "" -or $n -match "TO BE FILLED" -or $n -in @("DEFAULT STRING","NONE","0","SYSTEM SERIAL NUMBER","NOT SPECIFIED")) { $n = "NA" }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($n))
    $hex = -join ($hash[0..9] | ForEach-Object { $_.ToString("X2") })
    return ($hex -split '(.{4})' | Where-Object { $_ }) -join '-'
}

if (-not $MachineGuid) {
    $key = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64).OpenSubKey('SOFTWARE\Microsoft\Cryptography')
    $MachineGuid = if ($key) { $key.GetValue('MachineGuid') } else { $null }
}
if (-not $MachineGuid) { Write-Host "Could not read this PC's MachineGuid. Open Revit, run STING > Activate, and copy the code shown there." -ForegroundColor Red; exit 1 }

$code = Get-StingMachineCode $MachineGuid
Write-Host ""
Write-Host "  Your STING machine code:  $code" -ForegroundColor Green
Write-Host ""
try { Set-Clipboard -Value $code; Write-Host "  (copied to the clipboard)" } catch { }
try {
    $desk = [Environment]::GetFolderPath('Desktop')
    Set-Content -Path (Join-Path $desk 'MachineCode.txt') -Value "STING machine code for $env:COMPUTERNAME ($env:USERNAME): $code"
    Write-Host "  (saved to $desk\MachineCode.txt)"
} catch { }
Write-Host ""
Write-Host "  Send this code to Planscape. You will receive a StingTools.lic file valid for 90 days."
