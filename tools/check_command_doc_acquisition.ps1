# ══════════════════════════════════════════════════════════════════════════
#  check_command_doc_acquisition.ps1
#
#  Fails the build when an IExternalCommand that the dock panel DISPATCHES
#  reads commandData.Application directly instead of going through
#  ParameterHelpers.GetDoc / GetUIDoc / GetApp.
#
#  WHY THIS GATE EXISTS. StingCommandHandler.RunCommand<T> calls
#  cmd.Execute(NULL, ...) by design and expects commands to fall back to
#  StingCommandHandler.CurrentApp. A command that reads
#  commandData?.Application?.ActiveUIDocument?.Document gets null, returns on
#  its first line, and produces a DEAD BUTTON that logs a clean start/done
#  and no error. Nothing about it looks wrong: it compiles, it has a handler
#  case, it has a button, and the log says it ran.
#
#  This has now been fixed three separate times — 28 sites across
#  Commands/Drawing, then Commands/Cost, then 36 sites found only because a
#  user clicked a button and reported that nothing happened. Three rounds of
#  the same defect is a missing gate, not three mistakes.
#
#  Baseline is ZERO. If a command genuinely must read commandData directly,
#  give it the ?? StingCommandHandler.CurrentApp fallback on the same
#  expression and this script will accept it.
# ══════════════════════════════════════════════════════════════════════════
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$handler = Join-Path $root 'StingTools\UI\StingCommandHandler.cs'
if (-not (Test-Path $handler)) {
    Write-Host "SKIP: StingCommandHandler.cs not found"
    exit 0
}

# Every command class the panel can dispatch.
$handlerText = Get-Content $handler -Raw
$dispatched = [System.Collections.Generic.HashSet[string]]::new()
foreach ($m in [regex]::Matches($handlerText, 'RunCommand<(?:[\w\.]*\.)?(\w+)>')) {
    [void]$dispatched.Add($m.Groups[1].Value)
}
Write-Host "Dispatched command classes: $($dispatched.Count)"

# The broken shape: a null-conditional read of commandData with no fallback
# anywhere in the file.
$bad = [regex]'(?<![\w\.])(commandData|data|cd)\s*\?\s*\.\s*Application'
$violations = @()

Get-ChildItem -Path (Join-Path $root 'StingTools') -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object {
        $text = Get-Content $_.FullName -Raw
        if ($text -notmatch 'IExternalCommand') { return }
        if ($text -match 'StingCommandHandler\.CurrentApp') { return }   # has the fallback

        # Blank out comments before matching, preserving newlines so the
        # reported line numbers stay right. Without this the gate flags the
        # comments EXPLAINING the fix — which it did on its first run, against
        # the very commit that fixed the last two sites. A gate that cries wolf
        # over prose is a gate somebody disables.
        $text = [regex]::Replace($text, '(?s)/\*.*?\*/', { param($m) ($m.Value -replace '[^\r\n]', ' ') })
        $text = [regex]::Replace($text, '//[^\r\n]*',    { param($m) ' ' * $m.Value.Length })

        $classes = [regex]::Matches($text, 'class\s+(\w+)\s*:\s*IExternalCommand')
        $isDispatched = $false
        foreach ($c in $classes) {
            if ($dispatched.Contains($c.Groups[1].Value)) { $isDispatched = $true; break }
        }
        if (-not $isDispatched) { return }

        foreach ($hit in $bad.Matches($text)) {
            $line = ($text.Substring(0, $hit.Index) -split "`n").Count
            $violations += [pscustomobject]@{
                File = $_.FullName.Substring($root.Length + 1)
                Line = $line
            }
        }
    }

if ($violations.Count -eq 0) {
    Write-Host "PASS: every dispatched command resolves its document through ParameterHelpers."
    exit 0
}

Write-Host ""
Write-Host "FAIL: $($violations.Count) dispatched command site(s) read commandData.Application"
Write-Host "      directly. From the dock panel that is always null, so the button is dead:"
Write-Host "      it logs start/done, shows nothing, and throws nothing."
Write-Host ""
foreach ($v in $violations) { Write-Host ("  {0}:{1}" -f $v.File, $v.Line) }
Write-Host ""
Write-Host "Fix: ParameterHelpers.GetDoc(commandData) / GetUIDoc(commandData) / GetApp(commandData),"
Write-Host "     or add '?? StingTools.UI.StingCommandHandler.CurrentApp' to the expression."
exit 1
