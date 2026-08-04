<#
.SYNOPSIS
    Shared deploy-stamp helpers for Deploy-StingTools.ps1 and Start-RevitLocal.ps1.

.DESCRIPTION
    One deployed DLL is shared by every installed Revit version AND by every
    concurrent session working in this repo. Nothing used to record which branch
    produced it, so a sibling session could build over it and no one found out
    until behaviour disagreed with the source they were reading.

    That is not hypothetical. On 2026-08-03 a build of
    claude/fix-nonmodel-category-bindings landed on top of a deployed PR #550
    four minutes after that deploy, and an entire evening of manual testing ran
    against the wrong binary before the mismatch was noticed.

    The fix is a stamp file written beside the DLL at deploy time, plus a check
    at launch time that the DLL still hashes to what the stamp recorded.

    Deliberately pure ASCII, plain hyphens only. Windows PowerShell 5.1 reads
    .ps1 as ANSI when there is no BOM, so a stray em-dash or box-drawing
    character corrupts the parse with "string is missing the terminator". Do not
    tidy these into Unicode.
#>

$script:StingStampFileName = 'sting-deploy-stamp.json'
$script:StingStampSchema   = 1

function Get-StingAddinPaths {
    <#
    .SYNOPSIS
        Every StingTools.addin on this machine, user scope and machine scope.
    #>
    $roots = @(
        (Join-Path $env:APPDATA    'Autodesk\Revit\Addins'),
        (Join-Path $env:PROGRAMDATA 'Autodesk\Revit\Addins')
    )
    $found = @()
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $found += Get-ChildItem $root -Filter 'StingTools.addin' -Recurse -ErrorAction SilentlyContinue |
                  ForEach-Object { $_.FullName }
    }
    return $found
}

function Resolve-StingDeployedAssembly {
    <#
    .SYNOPSIS
        The DLL path the .addin manifests actually point at.

    .DESCRIPTION
        Read it from the manifest every time rather than hard-coding
        CompiledPlugin - the target has moved before and will move again, and a
        check that verifies a path Revit is not loading is worse than no check,
        because it reports success.

        Returns a hashtable: Assembly, Manifests, Conflicting.
        Conflicting is $true when the manifests disagree, which is itself worth
        surfacing - it means different Revit versions load different plugins.
    #>
    $manifests = Get-StingAddinPaths
    if (-not $manifests) {
        return @{ Assembly = $null; Manifests = @(); Conflicting = $false }
    }

    $targets = @()
    foreach ($m in $manifests) {
        try {
            $xml = [xml](Get-Content $m -Raw)
            foreach ($node in $xml.RevitAddIns.AddIn) {
                $asm = $node.Assembly
                if ($asm -and $asm -match 'StingTools\.dll$') { $targets += $asm }
            }
        } catch {
            Write-Verbose "Could not parse $m : $($_.Exception.Message)"
        }
    }

    $distinct = @($targets | Sort-Object -Unique)
    $assembly = $null
    if ($distinct.Count -gt 0) { $assembly = $distinct[0] }

    return @{
        Assembly    = $assembly
        Manifests   = $manifests
        Conflicting = ($distinct.Count -gt 1)
        AllTargets  = $distinct
    }
}

function Get-StingStampPath {
    param([Parameter(Mandatory = $true)][string] $AssemblyPath)
    return (Join-Path (Split-Path $AssemblyPath -Parent) $script:StingStampFileName)
}

function Get-StingFileSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path $Path)) { return $null }
    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-StingDeployStamp {
    <#
    .SYNOPSIS
        Record what was just deployed, beside the DLL that was deployed.
    #>
    param(
        [Parameter(Mandatory = $true)][string] $AssemblyPath,
        [Parameter(Mandatory = $true)][string] $Branch,
        [Parameter(Mandatory = $true)][string] $Commit,
        [string] $CommitSubject = '',
        [string] $Configuration = 'Release',
        [bool]   $DirtyWorktree = $false
    )

    $item = Get-Item $AssemblyPath
    $stamp = [ordered]@{
        schema          = $script:StingStampSchema
        branch          = $Branch
        commit          = $Commit
        commitSubject   = $CommitSubject
        configuration   = $Configuration
        dirtyWorktree   = $DirtyWorktree
        builtAtUtc      = (Get-Date).ToUniversalTime().ToString('o')
        builtBy         = "$env:USERDOMAIN\$env:USERNAME"
        assembly        = $item.Name
        assemblyLength  = $item.Length
        assemblySha256  = (Get-StingFileSha256 -Path $AssemblyPath)
    }

    $path = Get-StingStampPath -AssemblyPath $AssemblyPath
    ($stamp | ConvertTo-Json -Depth 4) | Out-File -FilePath $path -Encoding utf8
    return $path
}

function Test-StingDeployStamp {
    <#
    .SYNOPSIS
        Does the deployed DLL still match what the stamp recorded?

    .DESCRIPTION
        Returns a hashtable with Status, Message, and the parsed Stamp.

        Status is one of:
          Ok           - hash matches the stamp
          NoAssembly   - no .addin, or it points at a file that is not there
          NoStamp      - DLL present, stamp absent. After this feature ships,
                         that means something copied over the deploy WITHOUT
                         going through Deploy-StingTools.ps1 - which is exactly
                         the clobber this exists to catch.
          Mismatch     - stamp present but the DLL hashes differently. Same
                         cause, but the previous deploy did leave a stamp, so we
                         can name the branch that is now stale.
          Unreadable   - stamp exists but will not parse

    .PARAMETER AssemblyPath
        Check this DLL instead of resolving one from the .addin manifests. Only
        for self-tests - a real launch must check what Revit will actually load,
        which means resolving it from the manifest.
    #>
    param([string] $AssemblyPath)

    if ($AssemblyPath) {
        $resolved = @{ Assembly = $AssemblyPath; Manifests = @(); Conflicting = $false; AllTargets = @($AssemblyPath) }
    } else {
        $resolved = Resolve-StingDeployedAssembly
    }
    $asm = $resolved.Assembly

    if (-not $asm) {
        return @{ Status = 'NoAssembly'; Resolved = $resolved; Stamp = $null
                  Message = 'No StingTools.addin found, or none names a StingTools.dll assembly.' }
    }
    if (-not (Test-Path $asm)) {
        return @{ Status = 'NoAssembly'; Resolved = $resolved; Stamp = $null
                  Message = "The .addin points at a file that does not exist: $asm" }
    }

    $stampPath = Get-StingStampPath -AssemblyPath $asm
    if (-not (Test-Path $stampPath)) {
        return @{ Status = 'NoStamp'; Resolved = $resolved; Stamp = $null
                  Message = "No $script:StingStampFileName beside the deployed DLL." }
    }

    try {
        $stamp = Get-Content $stampPath -Raw | ConvertFrom-Json
    } catch {
        return @{ Status = 'Unreadable'; Resolved = $resolved; Stamp = $null
                  Message = "Could not parse $stampPath : $($_.Exception.Message)" }
    }

    $actual = Get-StingFileSha256 -Path $asm
    if ($actual -ne $stamp.assemblySha256) {
        return @{ Status = 'Mismatch'; Resolved = $resolved; Stamp = $stamp
                  Actual = $actual
                  Message = 'The deployed DLL does not match the recorded deploy.' }
    }

    return @{ Status = 'Ok'; Resolved = $resolved; Stamp = $stamp; Actual = $actual
              Message = 'Deployed DLL matches its stamp.' }
}
