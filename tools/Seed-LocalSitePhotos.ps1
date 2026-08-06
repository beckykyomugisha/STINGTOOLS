<#
.SYNOPSIS
    Seed the LOCAL Planscape stack with one project and one empty album, so the
    BCC site-photo checks have something to open.

.DESCRIPTION
    Idempotent. Running it twice creates nothing the second time - it matches on
    the fixture names below and reuses what it finds.

    Deliberately creates NO PHOTOS. An album with zero photos is the control
    case for the "empty" versus "could not load" distinction: the album must
    render as genuinely empty when the server answers, and as an error when it
    does not. Seeding photos would remove the only state worth checking.

    LOCAL ONLY. This talks to whatever -Url points at and it creates data, so
    the default is localhost and there is a guard against pointing it at a
    non-local host without -IKnowThisIsNotLocal. Production already has the
    owner's real project; it must not be touched.

    This script does not start, stop or reconfigure any container. If the API
    is not answering it reports that and exits.

    NOTE: pure ASCII, same reason as Start-RevitLocal.ps1 - Windows PowerShell
    5.1 reads .ps1 as ANSI when there is no BOM, so a stray em-dash breaks the
    parse. Do not tidy the hyphen separators into Unicode.

.PARAMETER Url
    API base URL. Default http://localhost:5000.

.PARAMETER Email
    Login for the docker stack. Default admin@planscape.demo.

.PARAMETER Password
    Password for the above. Default admin123.

.EXAMPLE
    .\tools\Seed-LocalSitePhotos.ps1
#>
[CmdletBinding()]
param(
    [string] $Url          = 'http://localhost:5000',
    [string] $Email        = 'admin@planscape.demo',
    [string] $Password     = 'admin123',
    [string] $ProjectName  = 'ZZ-FIXTURE Local Site Photos',
    [string] $AlbumName    = 'ZZ-FIXTURE Empty Album (no photos)',
    [switch] $IKnowThisIsNotLocal
)

$ErrorActionPreference = 'Stop'
$base = $Url.TrimEnd('/')

# --- Refuse to seed anything that is not obviously local --------------------
$hostName = ([Uri]$base).Host
if ($hostName -notin @('localhost', '127.0.0.1', '::1') -and -not $IKnowThisIsNotLocal) {
    Write-Host "REFUSING: $base is not localhost." -ForegroundColor Red
    Write-Host "This script CREATES DATA. Pass -IKnowThisIsNotLocal only if you are certain."
    exit 1
}

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Path,
        [object] $Body,
        [string] $Token
    )
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $args = @{
        Uri         = "$base$Path"
        Method      = $Method
        Headers     = $headers
        TimeoutSec  = 20
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $args['Body']        = ($Body | ConvertTo-Json -Depth 6)
        $args['ContentType'] = 'application/json'
    }
    return Invoke-RestMethod @args
}

# --- 1. Is the API up? ------------------------------------------------------
Write-Host "Stack    : $base"
try {
    $null = Invoke-WebRequest -Uri "$base/health/live" -TimeoutSec 5 -UseBasicParsing
    Write-Host "Health   : /health/live -> 200" -ForegroundColor Green
} catch {
    Write-Host "Health   : /health/live -> UNREACHABLE" -ForegroundColor Red
    Write-Host ""
    Write-Host "The local API is not answering. Start it yourself with:" -ForegroundColor Yellow
    Write-Host "    docker start docker-api-1"
    Write-Host ""
    Write-Host "This script will not start containers - other sessions share that stack."
    exit 1
}

# --- 2. Log in --------------------------------------------------------------
try {
    $auth  = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{ email = $Email; password = $Password }
    $token = $auth.accessToken
    if (-not $token) { $token = $auth.AccessToken }
    if (-not $token) { throw 'login succeeded but returned no accessToken' }
    Write-Host "Login    : $Email -> OK" -ForegroundColor Green
} catch {
    Write-Host "Login    : $Email -> FAILED" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)"
    Write-Host ""
    Write-Host "The docker stack's seeded account is admin@planscape.demo / admin123"
    Write-Host "(see Planscape.Server/docker). Pass -Email / -Password to override."
    exit 1
}

# --- 3. Project: reuse if the fixture already exists ------------------------
$projects = Invoke-Api -Method GET -Path '/api/projects' -Token $token
$project  = @($projects | Where-Object { $_.name -eq $ProjectName }) | Select-Object -First 1

if ($project) {
    Write-Host "Project  : reused '$ProjectName'" -ForegroundColor DarkGray
} else {
    $project = Invoke-Api -Method POST -Path '/api/projects' -Token $token -Body @{
        Name        = $ProjectName
        Description = 'Fixture for BCC site-photo verification. Safe to delete.'
    }
    Write-Host "Project  : created '$ProjectName'" -ForegroundColor Green
}
$projectId = $project.id
if (-not $projectId) { $projectId = $project.Id }
if (-not $projectId) { throw "could not read an id back from the project payload" }

# --- 4. Album: reuse if the fixture already exists --------------------------
$albums = Invoke-Api -Method GET -Path "/api/projects/$projectId/photo-albums" -Token $token
$album  = @($albums | Where-Object { $_.name -eq $AlbumName }) | Select-Object -First 1

if ($album) {
    Write-Host "Album    : reused '$AlbumName'" -ForegroundColor DarkGray
} else {
    # Visibility MUST be one of Internal | Members | Client | Distribution.
    # "Project" is not valid and the server 400s invalid_visibility.
    $album = Invoke-Api -Method POST -Path "/api/projects/$projectId/photo-albums" -Token $token -Body @{
        Name        = $AlbumName
        Description = 'Intentionally empty - the control case for empty vs could-not-load.'
        Visibility  = 'Members'
    }
    Write-Host "Album    : created '$AlbumName' (visibility Members, 0 photos)" -ForegroundColor Green
}
$albumId = $album.id
if (-not $albumId) { $albumId = $album.Id }

# --- 5. Report --------------------------------------------------------------
Write-Host ""
Write-Host "------------------------------------------------------------"
Write-Host "Project name : $ProjectName"
Write-Host "Project id   : $projectId"
Write-Host "Album name   : $AlbumName"
Write-Host "Album id     : $albumId"
Write-Host "Photos       : 0  (deliberately - this is the empty control case)"
Write-Host "------------------------------------------------------------"
Write-Host ""
Write-Host "To reach it in the BCC:" -ForegroundColor Cyan
Write-Host "  1. Launch Revit against this stack:"
Write-Host "       .\tools\Start-RevitLocal.ps1"
Write-Host "  2. Open the BIM Coordination Center."
Write-Host "  3. Connect / sign in as $Email"
Write-Host "  4. Select project '$ProjectName'."
Write-Host "  5. Go to the SITE PHOTOS tab -> Albums."
Write-Host ""
Write-Host "M2 (visibility default): click Create and read the visibility field."
Write-Host "    PASS = it reads 'Members'."
Write-Host ""
Write-Host "M3 (empty vs could-not-load): open '$AlbumName'."
Write-Host "    With the API UP   it must say the album is empty."
Write-Host "    With the API DOWN it must show a red load error, NOT 'Album is empty'."
Write-Host ""
Write-Host "M1 (failed list): relaunch with the API deliberately down --"
Write-Host "       docker stop docker-api-1     (your call - other sessions share it)"
Write-Host "       .\tools\Start-RevitLocal.ps1 -SkipHealthCheck"
Write-Host "    Albums must show a red error, NOT an empty list."
Write-Host ""
Write-Host "Re-running this script is a no-op." -ForegroundColor DarkGray
