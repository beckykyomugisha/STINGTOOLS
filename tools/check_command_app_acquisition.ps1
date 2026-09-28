<#
.SYNOPSIS
    Command-app gate -- a command must not read commandData.Application directly.

.DESCRIPTION
    StingCommandHandler.RunCommand<T> dispatches every dock-panel button with

        cmd.Execute(null, ref message, elSet);

    ExternalCommandData is null BY DESIGN -- the comment there says so: "commands use
    StingCommandHandler.CurrentApp as fallback. This avoids the fragile
    RuntimeHelpers.GetUninitializedObject reflection hack that breaks across Revit
    versions." So a command that reads commandData.Application gets null, and either
    throws a NullReferenceException or reports its own "no application" message. It
    works from the ribbon and fails from the panel, which is why it survives review.

    WHY THIS GATE EXISTS

    On 2026-09-18 a brand-new command (FixTagFamilyCategoriesCommand) shipped with
    exactly that bug and failed on its first press. Measuring the class found 50 sites
    across 32 files with the same mistake, all of them reachable from a dock-panel
    button -- including FamilyConformanceCheckCommand, whose button had therefore
    thrown on every press since Phase 185. RunCommand catches the NRE and logs "Most
    likely: command accessed commandData.Application without null check", so these had
    been failing visibly and unread for months.

    A reviewer cannot catch this by reading a diff: the line looks correct, and it IS
    correct for a ribbon command. Only the dispatch path makes it wrong. That is what a
    gate is for.

    THE RULE

    Read the application through one of the two resolvers, which know about both
    callers:

        ParameterHelpers.GetApp(commandData)        // throws if neither is available
        commandData.SafeApp()                       // returns null instead

    ALLOWED COUNT: 0, outside the resolvers themselves. There is no baseline, because
    the sweep took the count to zero -- a baseline would only be somewhere for the next
    one to hide.

.NOTES
    Exit 0 = clean. Exit 1 = at least one direct read.
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$srcRoot  = Join-Path $repoRoot 'StingTools'

# The two resolvers. They read the raw value on purpose and carry their own fallback;
# routing them through each other would be circular or merely absurd.
$resolvers = @(
    (Join-Path $srcRoot 'Core\ParameterHelpers.cs'),   # GetApp
    (Join-Path $srcRoot 'Core\IPanelCommand.cs')       # CommandContext.SafeApp
)

if (-not (Test-Path $srcRoot)) {
    Write-Host "Command-app gate: $srcRoot not found -- nothing to check." -ForegroundColor Yellow
    exit 0
}

$offenders = @()
$files = Get-ChildItem -Path $srcRoot -Recurse -Filter *.cs |
         Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

foreach ($f in $files) {
    if ($resolvers -contains $f.FullName) { continue }

    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $lineNo++
        if ($line -notmatch 'commandData\.Application') { continue }

        # A comment naming the mistake is how the fix explains itself -- including the
        # diagnostic message in RunCommand, which must keep naming the real symbol.
        $trimmed = $line.TrimStart()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('///') -or $trimmed.StartsWith('*')) { continue }

        # commandData?.Application inside a resolver-shaped null test is the safe form.
        if ($line -match 'commandData\?\.Application') { continue }

        $rel = $f.FullName.Substring($repoRoot.Length + 1)
        $offenders += [pscustomobject]@{ File = $rel; Line = $lineNo; Text = $trimmed }
    }
}

Write-Host ""
Write-Host "Command-app acquisition gate" -ForegroundColor Cyan
Write-Host "  .cs files scanned                     : $($files.Count)"
Write-Host "  Direct commandData.Application reads  : $($offenders.Count)"

if ($offenders.Count -gt 0) {
    Write-Host ""
    Write-Host "Command-app gate FAILED -- $($offenders.Count) direct read(s) of commandData.Application:" -ForegroundColor Red
    foreach ($o in $offenders) {
        Write-Host ("  {0}:{1}" -f $o.File, $o.Line) -ForegroundColor Red
        Write-Host ("      {0}" -f $o.Text) -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host @"
A dock-panel button dispatches with Execute(null, ...), so commandData is null and
this read returns null. It works from the ribbon and fails from the panel.

Use one of the resolvers instead:

    var app = ParameterHelpers.GetApp(commandData);   // throws if neither available
    var app = commandData.SafeApp();                  // returns null instead

There is no baseline for this gate. The count was swept to zero on 2026-09-18;
adding an exemption would just be somewhere for the next one to hide.
"@
    exit 1
}

Write-Host "Command-app gate OK -- every command resolves the application through a resolver." -ForegroundColor Green
exit 0
