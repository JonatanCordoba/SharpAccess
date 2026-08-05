#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot = $PWD,
    [string]$Revision = "HEAD",
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Runs Git and returns every output line as a string array.
function Invoke-GitLines(
    [string]$WorkingDirectory,
    [string[]]$Arguments,
    [string]$FailureMessage
) {
    $output = @(& git -C $WorkingDirectory @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $detail = @($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
        throw "$FailureMessage$([Environment]::NewLine)$detail"
    }

    return @($output | ForEach-Object { [string]$_ })
}

# Returns the only non-empty Git output line.
function Get-SingleGitLine(
    [string]$WorkingDirectory,
    [string[]]$Arguments,
    [string]$FailureMessage
) {
    $lines = @(
        Invoke-GitLines $WorkingDirectory $Arguments $FailureMessage |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
    )

    if ($lines.Count -ne 1) {
        throw "$FailureMessage Expected exactly one output line but received $($lines.Count)."
    }

    return ([string]$lines[0]).Trim()
}

# Writes one UTF-8 text file without a byte-order mark.
function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText(
        $Path,
        $Content,
        [Text.UTF8Encoding]::new($false)
    )
}

# Materializes one exact Git blob without text encoding or line-ending conversion.
function Export-GitBlob(
    [string]$WorkingDirectory,
    [string]$Blob,
    [string]$DestinationPath,
    [string]$TrackedPath
) {
    $destinationDirectory = [IO.Path]::GetDirectoryName($DestinationPath)
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "git"
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    [void]$startInfo.ArgumentList.Add("-C")
    [void]$startInfo.ArgumentList.Add($WorkingDirectory)
    [void]$startInfo.ArgumentList.Add("cat-file")
    [void]$startInfo.ArgumentList.Add("blob")
    [void]$startInfo.ArgumentList.Add($Blob)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $processStarted = $false

    try {
        $processStarted = $process.Start()
        if (-not $processStarted) {
            throw "Unable to start Git while exporting tracked file: $TrackedPath"
        }

        $errorReadTask = $process.StandardError.ReadToEndAsync()
        $fileStream = [IO.File]::Open(
            $DestinationPath,
            [IO.FileMode]::Create,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )

        try {
            $process.StandardOutput.BaseStream.CopyTo($fileStream)
            $fileStream.Flush($true)
        } finally {
            $fileStream.Dispose()
        }

        $process.WaitForExit()
        $errorText = $errorReadTask.GetAwaiter().GetResult()

        if ($process.ExitCode -ne 0) {
            Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction SilentlyContinue
            throw "Unable to materialize approved Git blob for ${TrackedPath}: $errorText"
        }
    } finally {
        if ($processStarted -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }

        $process.Dispose()
    }
}

# Returns whether a tracked path is forbidden from a release export.
function Test-ForbiddenExportPath([string]$Path) {
    $normalized = $Path.Replace("\", "/")
    $lower = $normalized.ToLowerInvariant()

    if ($lower -eq ".git" -or $lower.StartsWith(".git/", [StringComparison]::Ordinal)) {
        return $true
    }

    $forbiddenPrefixes = @(
        "artifacts/",
        "bin/",
        "obj/",
        ".vs/",
        ".idea/",
        "testresults/"
    )

    foreach ($prefix in $forbiddenPrefixes) {
        if ($lower.StartsWith($prefix, [StringComparison]::Ordinal)) {
            return $true
        }
    }

    $fileName = [IO.Path]::GetFileName($lower)
    if ($fileName -eq ".env") {
        return $true
    }

    $isLocalDatabase =
        $lower.EndsWith(".db", [StringComparison]::Ordinal) -or
        $lower.EndsWith(".sqlite", [StringComparison]::Ordinal) -or
        $lower.EndsWith(".sqlite3", [StringComparison]::Ordinal)
    $isApprovedMigrationFixture = $lower.StartsWith(
        "tests/fixtures/migrations/",
        [StringComparison]::Ordinal
    )

    return $isLocalDatabase -and -not $isApprovedMigrationFixture
}

# Parses one Git ls-tree line into a normalized record.
function ConvertFrom-GitTreeLine([string]$Line) {
    $match = [regex]::Match(
        $Line,
        '^(?<mode>[0-9]{6}) (?<type>[^ ]+) (?<blob>[0-9a-f]+)\t(?<path>.+)$'
    )

    if (-not $match.Success) {
        throw "Unexpected git ls-tree output: $Line"
    }

    return [pscustomobject]@{
        Mode = $match.Groups["mode"].Value
        Type = $match.Groups["type"].Value
        Blob = $match.Groups["blob"].Value
        Path = $match.Groups["path"].Value
    }
}

# Removes one directory without masking the primary result.
function Remove-DirectorySafely([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$solutionPath = Join-Path $root "SharpAccess.sln"
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Repository root is invalid: $root"
}

$topLevel = Get-SingleGitLine `
    $root `
    @("rev-parse", "--show-toplevel") `
    "Unable to resolve the Git work-tree root."
$resolvedTopLevel = (Resolve-Path -LiteralPath $topLevel).Path
if (-not [string]::Equals($resolvedTopLevel, $root, [StringComparison]::OrdinalIgnoreCase)) {
    throw "RepositoryRoot must be the Git work-tree root. Resolved root: $resolvedTopLevel"
}

$trackedStatus = @(
    Invoke-GitLines `
        $root `
        @("status", "--porcelain", "--untracked-files=no") `
        "Unable to inspect the tracked working tree."
)
if ($trackedStatus.Count -ne 0) {
    throw "Export dry run requires a clean tracked working tree."
}

$revisionSha = Get-SingleGitLine `
    $root `
    @("rev-parse", "$Revision^{commit}") `
    "Unable to resolve the requested export revision."
$sourceTree = Get-SingleGitLine `
    $root `
    @("rev-parse", "$revisionSha^{tree}") `
    "Unable to resolve the source tree."

$treeLines = @(
    Invoke-GitLines `
        $root `
        @("ls-tree", "-r", $revisionSha) `
        "Unable to enumerate the approved tracked tree."
)
$treeEntries = @($treeLines | ForEach-Object { ConvertFrom-GitTreeLine ([string]$_) })

$nonBlobEntries = @($treeEntries | Where-Object { $_.Type -ne "blob" })
if ($nonBlobEntries.Count -ne 0) {
    $unsupported = @($nonBlobEntries | ForEach-Object { $_.Path }) -join ", "
    throw "The approved tracked tree contains unsupported non-blob entries: $unsupported"
}

$forbiddenPaths = @(
    $treeEntries |
        Where-Object { Test-ForbiddenExportPath ([string]$_.Path) } |
        ForEach-Object { [string]$_.Path }
)
if ($forbiddenPaths.Count -ne 0) {
    throw "The approved tracked tree contains forbidden release-export paths: $($forbiddenPaths -join ', ')"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root "artifacts/release-candidate/export-dry-run"
} elseif (-not [IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $root $OutputRoot
}

$outputDirectory = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("sharpaccess-export-" + [Guid]::NewGuid().ToString("N"))
$exportPath = Join-Path $tempRoot "export"
$indexPath = Join-Path $tempRoot "equivalence.index"

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $exportPath -Force | Out-Null

    foreach ($entry in $treeEntries) {
        $exportedFile = Join-Path $exportPath ([string]$entry.Path)
        Export-GitBlob `
            $root `
            ([string]$entry.Blob) `
            $exportedFile `
            ([string]$entry.Path)
    }

    $exportedPaths = @(
        Get-ChildItem -LiteralPath $exportPath -File -Recurse |
            ForEach-Object {
                $_.FullName.Substring($exportPath.Length + 1).Replace("\", "/")
            } |
            Sort-Object
    )
    $trackedPaths = @($treeEntries | ForEach-Object { [string]$_.Path } | Sort-Object)

    $pathDifference = Compare-Object -ReferenceObject $trackedPaths -DifferenceObject $exportedPaths
    if ($null -ne $pathDifference) {
        $detail = @($pathDifference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join ", "
        throw "Exported file paths differ from the approved tracked tree: $detail"
    }

    $manifestFiles = [Collections.Generic.List[object]]::new()
    $previousIndexFile = $env:GIT_INDEX_FILE
    $env:GIT_INDEX_FILE = $indexPath

    try {
        foreach ($entry in $treeEntries) {
            $exportedFile = Join-Path $exportPath ([string]$entry.Path)
            if (-not (Test-Path -LiteralPath $exportedFile -PathType Leaf)) {
                throw "Exported file is missing: $($entry.Path)"
            }

            $exportedBlob = Get-SingleGitLine `
                $root `
                @("hash-object", "--no-filters", "--", $exportedFile) `
                "Unable to hash exported file: $($entry.Path)"
            if (-not [string]::Equals($exportedBlob, [string]$entry.Blob, [StringComparison]::Ordinal)) {
                throw "Exported file bytes differ from the approved Git blob: $($entry.Path)"
            }

            $cacheInfo = "$($entry.Mode),$exportedBlob,$($entry.Path)"
            $indexOutput = @(& git -C $root update-index --add --cacheinfo $cacheInfo 2>&1)
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to add exported file to the temporary equivalence index: $($entry.Path)$([Environment]::NewLine)$($indexOutput -join [Environment]::NewLine)"
            }

            $manifestFiles.Add([ordered]@{
                path = [string]$entry.Path
                mode = [string]$entry.Mode
                blob = [string]$entry.Blob
                sizeBytes = (Get-Item -LiteralPath $exportedFile).Length
            })
        }

        $exportTree = Get-SingleGitLine `
            $root `
            @("write-tree") `
            "Unable to write the temporary clean-root tree."
    } finally {
        if ($null -eq $previousIndexFile) {
            Remove-Item Env:GIT_INDEX_FILE -ErrorAction SilentlyContinue
        } else {
            $env:GIT_INDEX_FILE = $previousIndexFile
        }
    }

    if (-not [string]::Equals($exportTree, $sourceTree, [StringComparison]::Ordinal)) {
        throw "The temporary clean-root tree differs from the approved source tree. Source: $sourceTree; export: $exportTree"
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        revision = $revisionSha
        sourceTree = $sourceTree
        exportTree = $exportTree
        fileCount = $manifestFiles.Count
        files = $manifestFiles
    }

    $manifestPath = Join-Path $outputDirectory "export-manifest.json"
    $summaryPath = Join-Path $outputDirectory "export-summary.txt"
    Write-Utf8NoBom $manifestPath (($manifest | ConvertTo-Json -Depth 6) + "`n")
    Write-Utf8NoBom $summaryPath (@(
        "SharpAccess deterministic export dry run passed.",
        "Revision: $revisionSha",
        "Source tree: $sourceTree",
        "Export tree: $exportTree",
        "Tracked files: $($manifestFiles.Count)"
    ) -join "`n")

    Write-Host "Deterministic export dry run passed."
    Write-Host "Revision: $revisionSha"
    Write-Host "Tree: $sourceTree"
    Write-Host "Tracked files: $($manifestFiles.Count)"
    Write-Host "Evidence: $outputDirectory"
} finally {
    Remove-DirectorySafely $tempRoot
}
