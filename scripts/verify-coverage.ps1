#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$Path,
    [string]$Label = "Coverage",
    [decimal]$MinimumRate = 0.70,
    [decimal]$MinimumBranchRate = 0.00
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolves and validates the repository root independently of the caller's working directory.
function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $Candidate = $PSScriptRoot
        if (-not (Test-Path -LiteralPath (Join-Path $Candidate "SharpAccess.sln") -PathType Leaf)) {
            $Candidate = Join-Path $PSScriptRoot ".."
        }
    }

    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) {
        throw "Repository root is invalid."
    }

    return $resolved
}

# Converts either decimal rates or percent values into decimal rates.
function Convert-CoverageRate([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Coverage rate is missing."
    }

    $trimmed = $Value.Trim().TrimEnd('%')
    $number = [decimal]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
    if ($number -gt 1) {
        return $number / 100
    }

    return $number
}

# Converts XML count values into invariant-culture decimals.
function Convert-CoverageCount([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "Coverage count is missing."
    }

    return [decimal]::Parse($Value.Trim(), [System.Globalization.CultureInfo]::InvariantCulture)
}

# Reads the first matching XML attribute regardless of element depth or attribute casing.
function Read-AttributeValue([xml]$Document, [string[]]$AttributeNames) {
    foreach ($attributeName in $AttributeNames) {
        $normalizedAttributeName = $attributeName.ToLowerInvariant()
        $attribute = $Document.SelectSingleNode("//@*[translate(local-name(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz') = '$normalizedAttributeName']")
        if ($null -ne $attribute -and -not [string]::IsNullOrWhiteSpace($attribute.Value)) {
            return $attribute.Value
        }
    }

    return $null
}

# Reads the first matching XML element body regardless of depth or element casing.
function Read-ElementValue([xml]$Document, [string[]]$ElementNames) {
    foreach ($elementName in $ElementNames) {
        $normalizedElementName = $elementName.ToLowerInvariant()
        $node = $Document.SelectSingleNode("//*[translate(local-name(), 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz') = '$normalizedElementName']")
        if ($null -ne $node -and -not [string]::IsNullOrWhiteSpace($node.InnerText)) {
            return $node.InnerText
        }
    }

    return $null
}

# Reads a coverage rate from percent/rate fields or derives it from covered and total count fields.
function Read-Rate(
    [xml]$Document,
    [string[]]$AttributeNames,
    [string[]]$ElementNames,
    [string[]]$CoveredElementNames,
    [string[]]$TotalElementNames,
    [bool]$AllowMissingAsZero = $false) {
    $attributeValue = Read-AttributeValue $Document $AttributeNames
    if (-not [string]::IsNullOrWhiteSpace($attributeValue)) {
        return Convert-CoverageRate $attributeValue
    }

    $elementValue = Read-ElementValue $Document $ElementNames
    if (-not [string]::IsNullOrWhiteSpace($elementValue)) {
        return Convert-CoverageRate $elementValue
    }

    $coveredValue = Read-ElementValue $Document $CoveredElementNames
    $totalValue = Read-ElementValue $Document $TotalElementNames
    if (-not [string]::IsNullOrWhiteSpace($coveredValue) -and -not [string]::IsNullOrWhiteSpace($totalValue)) {
        $covered = Convert-CoverageCount $coveredValue
        $total = Convert-CoverageCount $totalValue
        if ($total -eq 0) {
            return [decimal]0
        }

        return $covered / $total
    }

    if ($AllowMissingAsZero) {
        return [decimal]0
    }

    throw "Coverage rate was not found."
}

$root = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($Path)) {
    $Path = Join-Path $root "artifacts/coverage/combined/coverage.xml"
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "XML file not found: $Path"
}

[xml]$document = Get-Content -LiteralPath $Path -Raw
$lineRate = Read-Rate `
    -Document $document `
    -AttributeNames @("line-rate", "lineRate", "lineCoverage", "sequenceCoverage") `
    -ElementNames @("LineCoverage", "LineRate", "SequenceCoverage") `
    -CoveredElementNames @("CoveredLines", "Coveredlines", "VisitedSequencePoints") `
    -TotalElementNames @("CoverableLines", "Coverablelines", "NumSequencePoints")
$branchRate = Read-Rate `
    -Document $document `
    -AttributeNames @("branch-rate", "branchRate", "branchCoverage") `
    -ElementNames @("BranchCoverage", "BranchRate") `
    -CoveredElementNames @("CoveredBranches", "Coveredbranches", "VisitedBranchPoints") `
    -TotalElementNames @("TotalBranches", "Totalbranches", "NumBranchPoints") `
    -AllowMissingAsZero $true

if ($lineRate -lt $MinimumRate) {
    throw "$Label line coverage is $lineRate; minimum required is $MinimumRate."
}

if ($branchRate -lt $MinimumBranchRate) {
    throw "$Label branch coverage is $branchRate; minimum required is $MinimumBranchRate."
}

Write-Host "$Label coverage gate passed: line $lineRate, branch $branchRate, minimum line $MinimumRate, minimum branch $MinimumBranchRate."
