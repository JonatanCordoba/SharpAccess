#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter()]
    [string]$RepositoryRoot = (Get-Location).Path,

    [Parameter()]
    [ValidateRange(1, 500)]
    [int]$Top = 80,

    [Parameter()]
    [ValidateRange(0, 5)]
    [int]$Context = 1
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$coveragePath = Join-Path $repositoryPath "artifacts/coverage/core/Cobertura.xml"
$outputDirectory = Join-Path $repositoryPath "artifacts/coverage/core"
$csvPath = Join-Path $outputDirectory "uncovered-branches.csv"
$reportPath = Join-Path $outputDirectory "uncovered-branches.md"

if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) {
    throw "Core coverage report was not found: $coveragePath"
}

[xml]$coverageDocument = Get-Content -LiteralPath $coveragePath -Raw
$coverageRoot = $coverageDocument.SelectSingleNode(
    "/*[local-name()='coverage']"
)

if ($null -eq $coverageRoot) {
    throw "The coverage file does not contain a coverage root element."
}

# Reads one required XML attribute and fails when coverage evidence is malformed.
function Get-RequiredAttribute {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlNode]$Node,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $attribute = $Node.Attributes[$Name]
    if ($null -eq $attribute) {
        throw "Required XML attribute '$Name' is missing from '$($Node.Name)'."
    }

    return $attribute.Value
}

$rows = [System.Collections.Generic.List[object]]::new()
$classNodes = $coverageRoot.SelectNodes(
    ".//*[local-name()='class']"
)

if ($null -eq $classNodes -or $classNodes.Count -eq 0) {
    throw "No class elements were found in the Core coverage report."
}

foreach ($class in $classNodes) {
    $file = Get-RequiredAttribute -Node $class -Name "filename"
    $className = Get-RequiredAttribute -Node $class -Name "name"
    $lineNodes = $class.SelectNodes(
        "./*[local-name()='lines']/*[local-name()='line']"
    )

    foreach ($line in $lineNodes) {
        $branchAttribute = $line.Attributes["branch"]
        if ($null -eq $branchAttribute -or $branchAttribute.Value -ne "true") {
            continue
        }

        $coverageAttribute = $line.Attributes["condition-coverage"]
        if ($null -eq $coverageAttribute) {
            continue
        }

        $conditionCoverage = $coverageAttribute.Value
        if ($conditionCoverage -notmatch '\((?<covered>\d+)\/(?<total>\d+)\)') {
            continue
        }

        $covered = [int]$Matches.covered
        $total = [int]$Matches.total
        $missing = $total - $covered

        if ($missing -le 0) {
            continue
        }

        $lineNumber = [int](Get-RequiredAttribute -Node $line -Name "number")
        $relativeFile = $file -replace '/', [IO.Path]::DirectorySeparatorChar
        $sourcePath = Join-Path $repositoryPath $relativeFile
        $sourceLine = ""
        $contextText = ""

        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $sourceLines = @(Get-Content -LiteralPath $sourcePath)

            if ($lineNumber -ge 1 -and $lineNumber -le $sourceLines.Count) {
                $sourceLine = $sourceLines[$lineNumber - 1].Trim()
                $firstLine = [Math]::Max(1, $lineNumber - $Context)
                $lastLine = [Math]::Min($sourceLines.Count, $lineNumber + $Context)
                $contextLines = [System.Collections.Generic.List[string]]::new()

                for ($index = $firstLine; $index -le $lastLine; $index++) {
                    $contextLines.Add(
                        ("{0,5}: {1}" -f $index, $sourceLines[$index - 1])
                    )
                }

                $contextText = $contextLines -join [Environment]::NewLine
            }
        }

        $rows.Add(
            [pscustomobject]@{
                Missing = $missing
                Covered = $covered
                Total = $total
                File = $file
                Class = $className
                Line = $lineNumber
                ConditionCoverage = $conditionCoverage
                Source = $sourceLine
                Context = $contextText
            }
        )
    }
}

if ($rows.Count -eq 0) {
    throw "No uncovered Core branches were found in $coveragePath."
}

$sortRules = @(
    @{ Expression = "Missing"; Descending = $true },
    @{ Expression = "File"; Descending = $false },
    @{ Expression = "Line"; Descending = $false }
)

$ranked = @($rows | Sort-Object -Property $sortRules)

$branchRate = [double](Get-RequiredAttribute -Node $coverageRoot -Name "branch-rate")
$branchesCovered = [int](Get-RequiredAttribute -Node $coverageRoot -Name "branches-covered")
$branchesValid = [int](Get-RequiredAttribute -Node $coverageRoot -Name "branches-valid")
$requiredCovered = [int][Math]::Ceiling(0.75 * $branchesValid)
$remaining = [Math]::Max(0, $requiredCovered - $branchesCovered)

$ranked |
    Select-Object -Property Missing, Covered, Total, File, Class, Line, ConditionCoverage, Source |
    Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine("# Core uncovered branch report")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("- Branch rate: $branchRate")
[void]$builder.AppendLine("- Covered branches: $branchesCovered")
[void]$builder.AppendLine("- Total branches: $branchesValid")
[void]$builder.AppendLine("- Covered branches required for 75%: $requiredCovered")
[void]$builder.AppendLine("- Additional covered branches required: $remaining")
[void]$builder.AppendLine("")
[void]$builder.AppendLine("## Ranked uncovered branch locations")
[void]$builder.AppendLine("")

foreach ($row in @($ranked | Select-Object -First $Top)) {
    [void]$builder.AppendLine(
        "### $($row.File):$($row.Line) - missing $($row.Missing) of $($row.Total)"
    )
    [void]$builder.AppendLine("")
    [void]$builder.AppendLine("- Class: $($row.Class)")
    [void]$builder.AppendLine("- Condition coverage: $($row.ConditionCoverage)")

    if (-not [string]::IsNullOrWhiteSpace($row.Source)) {
        [void]$builder.AppendLine("- Source: $($row.Source)")
    }

    if (-not [string]::IsNullOrWhiteSpace($row.Context)) {
        [void]$builder.AppendLine("")
        [void]$builder.AppendLine("Context:")
        [void]$builder.AppendLine($row.Context)
    }

    [void]$builder.AppendLine("")
}

[IO.File]::WriteAllText(
    $reportPath,
    $builder.ToString(),
    [Text.UTF8Encoding]::new($false)
)

Write-Host ""
Write-Host "Core branch coverage: $branchesCovered / $branchesValid ($branchRate)"
Write-Host "Covered branches required for 75%: $requiredCovered"
Write-Host "Additional covered branches required: $remaining"
Write-Host ""
Write-Host "Top uncovered locations:"

$displayCount = [Math]::Min($Top, 30)
$displayRows = $ranked |
    Select-Object -First $displayCount -Property Missing, Covered, Total, File, Line, Source

$displayRows | Format-Table -AutoSize

Write-Host ""
Write-Host "Detailed report: $reportPath"
Write-Host "CSV report:      $csvPath"
