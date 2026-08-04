<#
.SYNOPSIS
    Launch Revit with StingTools pointed at a LOCAL Planscape server (or
    explicitly at production, with -Prod).

.DESCRIPTION
    The plugin resolves its API base URL in this order (PlanscapeServerClient
    .Settings.cs, ResolveDefaultServerUrl):

        1. STING_PLANSCAPE_URL environment variable
        2. %APPDATA%\StingTools\planscape_server.json  ("serverUrl")
        3. baked corporate default (BakedDefaultServerUrl)

    The resolved value is cached in _cachedDefaultUrl for the lifetime of the
    process, so changing the target always means restarting Revit. That is why
    this is a launcher and not a toggle.

    This script sets (1) for the launched process ONLY. The saved production
    pointer in (2) is never touched, so there is no "change it back" step to
    forget - close Revit and the override is gone.

    Do NOT use setx or a user-level environment variable for this. That
    persists, and a forgotten local override silently points a real session at
    a dev database. Avoiding that is the entire point of the design; a script
    that writes planscape_server.json would have the same failure mode as the
    hand-editing it replaces.

    NOTE: this file is deliberately pure ASCII, and the separators below are
    plain hyphens for the same reason. Windows PowerShell 5.1 reads .ps1 as
    ANSI when there is no BOM, so a stray em-dash or box-drawing character
    corrupts the parse with "string is missing the terminator". Do not tidy
    these back into Unicode.

.PARAMETER Revit
    Revit version to launch. Defaults to the newest installed.

.PARAMETER Url
    API base URL. Defaults to http://localhost:5000 (the docker stack's
    published port for docker-api-1). Cannot be combined with -Prod.

.PARAMETER Prod
    Launch with NO override, so the plugin falls through to the saved pointer
    in planscape_server.json. Use this to return to production without editing
    a file by hand. Any STING_PLANSCAPE_URL inherited from the current shell is
    explicitly cleared for the child process, so -Prod means production even if
    you ran this script without it earlier in the same window.

.PARAMETER SkipHealthCheck
    Launch even if the URL does not answer. Use when you deliberately want to
    start with the API down - which is exactly what the site-photo "could not
    load" checks need.

.PARAMETER ExpectBranch
    Refuse to launch unless the deployed plugin was built from this branch.
    Use it when a run must test one specific branch and a wrong binary would
    waste the session - see the deploy check below.

.PARAMETER Force
    Skip the confirmation prompt when Revit is already running, AND downgrade a
    failed deploy check from a refusal to a red warning. Both are "I know what I
    am doing" escapes; neither is the default, on purpose.

.NOTES
    DEPLOY CHECK

    One StingTools.dll is shared by every installed Revit version and by every
    concurrent session working in this repo. On 2026-08-03 a sibling session
    built claude/fix-nonmodel-category-bindings over a freshly deployed PR #550
    four minutes later, nothing announced it, and an entire evening of manual
    testing ran against the wrong binary.

    So before launching, this script resolves the DLL that the .addin manifests
    actually point at - read fresh every time, because that path has moved
    before - and checks it against the sting-deploy-stamp.json that
    Deploy-StingTools.ps1 writes beside it. A missing stamp or a hash mismatch
    means something copied over the deploy without going through the deploy
    script, and the run is refused (exit 3) rather than silently testing
    whatever happens to be on disk.

    The stamp is read for identity too: the branch, commit and build time are
    printed on every launch, so "which code am I about to test" is answered
    before Revit starts rather than guessed afterwards.

.EXAMPLE
    .\tools\Start-RevitLocal.ps1
    Launch the newest installed Revit against http://localhost:5000.

.EXAMPLE
    .\tools\Start-RevitLocal.ps1 -Revit 2025 -SkipHealthCheck
    Launch Revit 2025 against the local API even if it is down.

.EXAMPLE
    .\tools\Start-RevitLocal.ps1 -Prod
    Launch with no override - the plugin uses the saved production pointer.
#>
[CmdletBinding(DefaultParameterSetName = 'Local')]
param(
    [string] $Revit,

    [Parameter(ParameterSetName = 'Local')]
    [string] $Url = 'http://localhost:5000',

    [Parameter(ParameterSetName = 'Prod', Mandatory = $true)]
    [switch] $Prod,

    [Parameter(ParameterSetName = 'Local')]
    [switch] $SkipHealthCheck,

    [string] $ExpectBranch,

    [switch] $Force
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'StingDeploy.Common.ps1')

# --- Verify the deployed plugin before anything else --------------------------
# This runs first because every other check below is about the SERVER the plugin
# talks to. None of it matters if the plugin itself is not the code under test.
$check = Test-StingDeployStamp

Write-Host ""
if ($check.Status -eq 'Ok') {
    $s = $check.Stamp
    $dirty = ''
    if ($s.dirtyWorktree) { $dirty = '  (built from a DIRTY worktree)' }
    Write-Host "Plugin   : $($s.branch) @ $($s.commit)$dirty" -ForegroundColor Green
    Write-Host "           deployed $($s.builtAtUtc) UTC by $($s.builtBy) [$($s.configuration)]"
    Write-Host "           $($check.Resolved.Assembly)"
    if ($s.commitSubject) { Write-Host "           $($s.commitSubject)" -ForegroundColor DarkGray }
} else {
    Write-Host "DEPLOY CHECK FAILED: $($check.Status)" -ForegroundColor Red
    Write-Host $check.Message -ForegroundColor Red
    if ($check.Resolved.Assembly) {
        Write-Host "  Assembly : $($check.Resolved.Assembly)"
    }
    if ($check.Stamp) {
        Write-Host "  Stamp says   : $($check.Stamp.branch) @ $($check.Stamp.commit), $($check.Stamp.builtAtUtc) UTC"
        Write-Host "  Stamp  sha256: $($check.Stamp.assemblySha256)"
        Write-Host "  Actual sha256: $($check.Actual)" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Something replaced the deployed plugin without going through" -ForegroundColor Yellow
    Write-Host "Deploy-StingTools.ps1. That target is shared by every Revit version" -ForegroundColor Yellow
    Write-Host "and every session in this repo, so another session's build can land" -ForegroundColor Yellow
    Write-Host "on it silently - which is exactly what this check exists to catch." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Re-deploy the branch you mean to test:"
    Write-Host "    .\tools\Deploy-StingTools.ps1"
    Write-Host ""
    Write-Host "Or pass -Force to launch anyway and accept that you do not know"
    Write-Host "which code you are testing."
    if (-not $Force) { exit 3 }
    Write-Host ""
    Write-Host "-Force given - launching an UNVERIFIED plugin." -ForegroundColor Red
}

# Branch assertion is separate from the integrity check: the stamp can be
# perfectly valid and still be the wrong branch for this run.
if ($ExpectBranch) {
    $actualBranch = $null
    if ($check.Stamp) { $actualBranch = $check.Stamp.branch }
    if ($actualBranch -ne $ExpectBranch) {
        Write-Host ""
        Write-Host "BRANCH MISMATCH" -ForegroundColor Red
        Write-Host "  Expected : $ExpectBranch"
        Write-Host "  Deployed : $(if ($actualBranch) { $actualBranch } else { '(unknown - no valid stamp)' })" -ForegroundColor Yellow
        if (-not $Force) { exit 3 }
        Write-Host "-Force given - continuing against the wrong branch." -ForegroundColor Red
    }
}

if ($check.Resolved.Conflicting) {
    Write-Host ""
    Write-Host "WARNING: the .addin manifests do not agree on one assembly." -ForegroundColor Yellow
    Write-Host "Different Revit versions will load different plugins:" -ForegroundColor Yellow
    foreach ($t in $check.Resolved.AllTargets) { Write-Host "    $t" }
}

# --- Locate Revit ----------------------------------------------------------
$autodesk = 'C:\Program Files\Autodesk'
$installed = @()
if (Test-Path $autodesk) {
    $installed = Get-ChildItem $autodesk -Directory |
        Where-Object { $_.Name -match '^Revit [0-9][0-9][0-9][0-9]$' } |
        ForEach-Object { $_.Name.Substring(6) } |
        Sort-Object -Descending
}
if (-not $installed) { throw "No Revit installation found under $autodesk." }

if (-not $Revit) { $Revit = $installed[0] }
if ($installed -notcontains $Revit) {
    throw "Revit $Revit not found. Installed: $($installed -join ', ')"
}

$exe = Join-Path $autodesk "Revit $Revit\Revit.exe"
if (-not (Test-Path $exe)) { throw "Missing executable: $exe" }

# --- Read the saved (production) pointer so the active target is unambiguous -
$settings = Join-Path $env:APPDATA 'StingTools\planscape_server.json'
$savedUrl = '(none saved)'
if (Test-Path $settings) {
    try {
        $savedUrl = (Get-Content $settings -Raw | ConvertFrom-Json).serverUrl
        if (-not $savedUrl) { $savedUrl = '(no serverUrl key)' }
    } catch { $savedUrl = '(unreadable)' }
}

# --- Warn if Revit is already up -------------------------------------------
# The override is applied to the process this script starts. It cannot reach a
# Revit that is already running, and _cachedDefaultUrl means that instance will
# not re-read the setting either. Launching anyway gives you two Revits on two
# different servers, which is a confusing way to lose an hour. Warn, then let
# the operator decide - never kill anything.
$running = @(Get-Process -Name 'Revit' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0 -and -not $Force) {
    Write-Host ""
    Write-Host "WARNING: Revit is already running (PID $($running.Id -join ', '))." -ForegroundColor Yellow
    Write-Host "The override applies only to a NEWLY launched process, so the running"
    Write-Host "instance keeps whatever target it started with. Continuing starts a"
    Write-Host "SECOND Revit, and the two will talk to different servers."
    Write-Host ""
    Write-Host "Close the running instance first if you meant to switch its target."
    Write-Host ""
    $answer = Read-Host 'Launch a second instance anyway? [y/N]'
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host "Aborted - nothing launched." -ForegroundColor Yellow
        exit 2
    }
}

# --- Fail fast if the local API is not answering ---------------------------
if (-not $Prod -and -not $SkipHealthCheck) {
    $health = $Url.TrimEnd('/') + '/health/live'
    try {
        $r = Invoke-WebRequest -Uri $health -TimeoutSec 5 -UseBasicParsing
        Write-Host "Health   : $health -> $($r.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Host "Health   : $health -> UNREACHABLE" -ForegroundColor Red
        Write-Host ""
        Write-Host "The local API is not answering. Start it with:" -ForegroundColor Yellow
        Write-Host "    docker start docker-api-1"
        Write-Host ""
        Write-Host "Or pass -SkipHealthCheck if you meant to start with it down."
        exit 1
    }
}

# --- Launch ----------------------------------------------------------------
Write-Host ""
Write-Host "Revit    : $Revit"
if ($Prod) {
    # Clear an inherited override so -Prod is honest even in a shell that ran
    # the local form earlier. Start-Process inherits this process's environment.
    Remove-Item Env:STING_PLANSCAPE_URL -ErrorAction SilentlyContinue
    Write-Host "Override : none (STING_PLANSCAPE_URL cleared for this launch)" -ForegroundColor Cyan
    Write-Host "Target   : $savedUrl  (from planscape_server.json)"
} else {
    $env:STING_PLANSCAPE_URL = $Url
    Write-Host "Override : STING_PLANSCAPE_URL = $Url  (this process only)" -ForegroundColor Cyan
    Write-Host "Saved    : $savedUrl  (untouched)"
}
Write-Host ""

Start-Process -FilePath $exe

if ($Prod) {
    Write-Host "Launched against the saved pointer. No override was set." -ForegroundColor Green
} else {
    Write-Host "Launched. Close Revit and the override is gone - nothing to undo." -ForegroundColor Green
}
