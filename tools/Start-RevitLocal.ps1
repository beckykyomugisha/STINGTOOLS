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

.PARAMETER Force
    Skip the confirmation prompt when Revit is already running.

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

    [switch] $Force
)

$ErrorActionPreference = 'Stop'

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
