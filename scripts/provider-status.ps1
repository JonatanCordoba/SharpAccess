#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Reads and validates the authoritative active package and provider status catalog.
function Get-SharpAccessPackageCatalog([string]$RepositoryRoot) {
    $manifestPath = Join-Path $RepositoryRoot "eng/ProviderStatus.props"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Provider status manifest is missing: $manifestPath"
    }

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $allowedStatuses = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@("Supported", "Internal implementation in progress", "Roadmap", "Unsupported"),
        [StringComparer]::Ordinal)

    $definitions = @(
        @{ PackageId = "SharpAccess.Core"; ProjectPath = "src/SharpAccess.Core/SharpAccess.Core.csproj"; Property = "SharpAccessCoreStatus"; RegistrationMethod = "" },
        @{ PackageId = "SharpAccess.Sqlite"; ProjectPath = "providers/SharpAccess.Sqlite/SharpAccess.Sqlite.csproj"; Property = "SharpAccessSqliteStatus"; RegistrationMethod = "AddSqliteAccess" },
        @{ PackageId = "SharpAccess.Postgres"; ProjectPath = "providers/SharpAccess.Postgres/SharpAccess.Postgres.csproj"; Property = "SharpAccessPostgresStatus"; RegistrationMethod = "AddPostgresAccess" }
    )

    foreach ($definition in $definitions) {
        $node = $manifest.SelectSingleNode("//PropertyGroup/$($definition.Property)")
        if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
            throw "Provider status property $($definition.Property) is missing from $manifestPath."
        }

        $status = $node.InnerText.Trim()
        if (-not $allowedStatuses.Contains($status)) {
            throw "Provider status '$status' is invalid for $($definition.PackageId)."
        }

        $projectPath = Join-Path $RepositoryRoot $definition.ProjectPath
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
            throw "Project from provider status catalog is missing: $projectPath"
        }

        [pscustomobject]@{
            PackageId = $definition.PackageId
            ProjectPath = $definition.ProjectPath
            Status = $status
            RegistrationMethod = $definition.RegistrationMethod
        }
    }
}

# Returns only packages whose authoritative status permits stable package creation.
function Get-SharpAccessSupportedPackageCatalog([string]$RepositoryRoot) {
    return @(Get-SharpAccessPackageCatalog -RepositoryRoot $RepositoryRoot |
        Where-Object { $_.Status -eq "Supported" })
}
