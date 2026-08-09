#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $RepositoryRoot,

    [Parameter(Mandatory)]
    [string] $ArtifactRoot,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $ReleaseSha,

    [Parameter(Mandatory)]
    [string] $EvidenceIndexRelativePath,

    [Parameter(Mandatory)]
    [string] $ChecksumsRelativePath,

    [string] $ExpectedRepositoryUrl = 'https://github.com/JonatanCordoba/SharpAccess',

    [string] $GitHubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ContainedPath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Description path must be relative: '$RelativePath'."
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $rootFull $RelativePath))
    $prefix = $rootFull + [System.IO.Path]::DirectorySeparatorChar

    if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description path escapes artifact root: '$RelativePath'."
    }

    $candidate
}

function Get-JsonMatches {
    param(
        [Parameter(Mandatory)]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $PropertyNamePattern,

        [Parameter(Mandatory)]
        [string] $ExpectedValue
    )

    $results = [System.Collections.Generic.List[string]]::new()

    function Visit-Value {
        param(
            [object] $Node,
            [string] $Path
        )

        if ($null -eq $Node) {
            return
        }

        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $Node.PSObject.Properties) {
                $childPath = if ([string]::IsNullOrWhiteSpace($Path)) { $property.Name } else { "$Path.$($property.Name)" }
                if ($property.Name -match $PropertyNamePattern -and [string]$property.Value -ieq $ExpectedValue) {
                    $results.Add($childPath)
                }
                Visit-Value -Node $property.Value -Path $childPath
            }
            return
        }

        if ($Node -is [System.Collections.IDictionary]) {
            foreach ($key in $Node.Keys) {
                $name = [string]$key
                $childPath = if ([string]::IsNullOrWhiteSpace($Path)) { $name } else { "$Path.$name" }
                $child = $Node[$key]
                if ($name -match $PropertyNamePattern -and [string]$child -ieq $ExpectedValue) {
                    $results.Add($childPath)
                }
                Visit-Value -Node $child -Path $childPath
            }
            return
        }

        if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
            $index = 0
            foreach ($child in $Node) {
                Visit-Value -Node $child -Path "$Path[$index]"
                $index++
            }
        }
    }

    Visit-Value -Node $Value -Path ''
    @($results)
}

function Test-JsonContainsScalar {
    param(
        [Parameter(Mandatory)]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $ExpectedValue
    )

    $found = $false

    function Visit-Scalar {
        param([object] $Node)

        if ($script:found -or $null -eq $Node) {
            return
        }

        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $Node.PSObject.Properties) {
                if ($property.Name -ceq $ExpectedValue) {
                    $script:found = $true
                    return
                }
                Visit-Scalar -Node $property.Value
            }
            return
        }

        if ($Node -is [System.Collections.IDictionary]) {
            foreach ($key in $Node.Keys) {
                if ([string]$key -ceq $ExpectedValue) {
                    $script:found = $true
                    return
                }
                Visit-Scalar -Node $Node[$key]
            }
            return
        }

        if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
            foreach ($child in $Node) {
                Visit-Scalar -Node $child
            }
            return
        }

        if ([string]$Node -ceq $ExpectedValue) {
            $script:found = $true
        }
    }

    $script:found = $false
    Visit-Scalar -Node $Value
    $result = $script:found
    Remove-Variable -Name found -Scope Script -ErrorAction SilentlyContinue
    $result
}

function Read-NuspecMetadata {
    param(
        [Parameter(Mandatory)]
        [string] $PackagePath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($nuspecEntries.Count -ne 1) {
            throw "Package '$PackagePath' contains $($nuspecEntries.Count) .nuspec files; exactly one is required."
        }

        $stream = $nuspecEntries[0].Open()
        try {
            $settings = [System.Xml.XmlReaderSettings]::new()
            $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $reader = [System.Xml.XmlReader]::Create($stream, $settings)
            try {
                $xml = [System.Xml.XmlDocument]::new()
                $xml.XmlResolver = $null
                $xml.Load($reader)
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $metadata = $xml.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "Package '$PackagePath' does not contain nuspec metadata."
    }

    $id = $metadata.SelectSingleNode("*[local-name()='id']")
    $versionNode = $metadata.SelectSingleNode("*[local-name()='version']")
    $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
    $dependencyNodes = @($metadata.SelectNodes("*[local-name()='dependencies']//*[local-name()='dependency']"))
    $packageTypeNodes = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']"))

    [pscustomobject]@{
        Id                   = if ($null -eq $id) { '' } else { [string]$id.InnerText }
        Version              = if ($null -eq $versionNode) { '' } else { [string]$versionNode.InnerText }
        RepositoryUrl        = if ($null -eq $repository) { '' } else { [string]$repository.Attributes['url'].Value }
        RepositoryCommit     = if ($null -eq $repository -or $null -eq $repository.Attributes['commit']) { '' } else { [string]$repository.Attributes['commit'].Value }
        LicenseExpression    = if ($null -eq $license) { '' } else { [string]$license.InnerText }
        Readme               = if ($null -eq $readme) { '' } else { [string]$readme.InnerText }
        PackageTypes         = @($packageTypeNodes | ForEach-Object { [string]$_.Attributes['name'].Value })
        Dependencies         = @($dependencyNodes | ForEach-Object {
            [pscustomobject]@{
                Id      = [string]$_.Attributes['id'].Value
                Version = [string]$_.Attributes['version'].Value
            }
        })
    }
}

function Add-ChecksumEntry {
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, string]] $Map,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Hash
    )

    $normalizedHash = $Hash.Trim().ToLowerInvariant()
    if ($normalizedHash.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
        $normalizedHash = $normalizedHash.Substring(7)
    }
    if ($normalizedHash -cnotmatch '^[0-9a-f]{64}$') {
        return
    }

    $name = [System.IO.Path]::GetFileName($Path.Trim().Trim('"'))
    if ([string]::IsNullOrWhiteSpace($name)) {
        return
    }

    if ($Map.ContainsKey($name) -and $Map[$name] -cne $normalizedHash) {
        throw "Checksum manifest contains conflicting hashes for '$name'."
    }

    $Map[$name] = $normalizedHash
}

function Read-ChecksumManifest {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $map = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $raw = Get-Content -LiteralPath $Path -Raw

    if ([System.IO.Path]::GetExtension($Path) -ieq '.json') {
        $json = $raw | ConvertFrom-Json -Depth 100

        function Visit-ChecksumJson {
            param([object] $Node)

            if ($null -eq $Node) {
                return
            }

            if ($Node -is [System.Management.Automation.PSCustomObject]) {
                $properties = @{}
                foreach ($property in $Node.PSObject.Properties) {
                    $properties[$property.Name.ToLowerInvariant()] = $property.Value
                    if ([string]$property.Value -match '^(?:sha256:)?[0-9A-Fa-f]{64}$' -and $property.Name -match '\.(?:nupkg|snupkg)$') {
                        Add-ChecksumEntry -Map $map -Path $property.Name -Hash ([string]$property.Value)
                    }
                }

                $pathValue = $null
                foreach ($name in @('path', 'file', 'filename', 'name')) {
                    if ($properties.ContainsKey($name)) {
                        $pathValue = [string]$properties[$name]
                        break
                    }
                }

                $hashValue = $null
                foreach ($name in @('sha256', 'hash', 'digest')) {
                    if ($properties.ContainsKey($name)) {
                        $hashValue = [string]$properties[$name]
                        break
                    }
                }

                if (-not [string]::IsNullOrWhiteSpace($pathValue) -and -not [string]::IsNullOrWhiteSpace($hashValue)) {
                    Add-ChecksumEntry -Map $map -Path $pathValue -Hash $hashValue
                }

                foreach ($property in $Node.PSObject.Properties) {
                    Visit-ChecksumJson -Node $property.Value
                }
                return
            }

            if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
                foreach ($child in $Node) {
                    Visit-ChecksumJson -Node $child
                }
            }
        }

        Visit-ChecksumJson -Node $json
        return $map
    }

    foreach ($line in ($raw -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed -match '^([0-9A-Fa-f]{64})\s+\*?(.+)$') {
            Add-ChecksumEntry -Map $map -Path $Matches[2] -Hash $Matches[1]
            continue
        }

        if ($trimmed -match '^SHA256\s*\((.+)\)\s*=\s*([0-9A-Fa-f]{64})$') {
            Add-ChecksumEntry -Map $map -Path $Matches[1] -Hash $Matches[2]
            continue
        }
    }

    $map
}

function Write-WorkflowOutput {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($GitHubOutput)) {
        return
    }

    Add-Content -LiteralPath $GitHubOutput -Value "$Name=$Value" -Encoding utf8NoBOM
}

$resolvedRepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath $resolvedRepositoryRoot -PathType Container)) {
    throw "Repository root does not exist: $resolvedRepositoryRoot"
}

$packagePolicyPath = Join-Path $resolvedRepositoryRoot 'Directory.Build.props'
if (-not (Test-Path -LiteralPath $packagePolicyPath -PathType Leaf)) {
    throw "Tracked package policy does not exist: $packagePolicyPath"
}

[xml]$packagePolicy = Get-Content -LiteralPath $packagePolicyPath -Raw
$licenseNodes = @(
    $packagePolicy.SelectNodes('/Project/PropertyGroup/PackageLicenseExpression')
)

if ($licenseNodes.Count -ne 1) {
    throw "Directory.Build.props must declare exactly one PackageLicenseExpression; found $($licenseNodes.Count)."
}

$expectedLicenseExpression = [string]$licenseNodes[0].InnerText
$expectedLicenseExpression = $expectedLicenseExpression.Trim()

if ([string]::IsNullOrWhiteSpace($expectedLicenseExpression)) {
    throw 'Directory.Build.props declares an empty PackageLicenseExpression.'
}

$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "Downloaded artifact root does not exist: $resolvedArtifactRoot"
}

$ReleaseSha = $ReleaseSha.ToLowerInvariant()
$expectedPackageIds = @('SharpAccess.Core', 'SharpAccess.Sqlite', 'SharpAccess.Postgres')
$expectedRuntimeNames = @($expectedPackageIds | ForEach-Object { "$($_).$Version.nupkg" })
$expectedSymbolNames = @($expectedPackageIds | ForEach-Object { "$($_).$Version.snupkg" })
$expectedAllNames = @($expectedRuntimeNames + $expectedSymbolNames)

$runtimePackages = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File -Filter '*.nupkg')
$symbolPackages = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File -Filter '*.snupkg')

$runtimeNames = @($runtimePackages | ForEach-Object Name | Sort-Object)
$symbolNames = @($symbolPackages | ForEach-Object Name | Sort-Object)
if (Compare-Object -ReferenceObject ($expectedRuntimeNames | Sort-Object) -DifferenceObject $runtimeNames) {
    throw "Runtime package cohort mismatch. Expected exactly: $($expectedRuntimeNames -join ', '). Found: $($runtimeNames -join ', ')."
}
if (Compare-Object -ReferenceObject ($expectedSymbolNames | Sort-Object) -DifferenceObject $symbolNames) {
    throw "Symbol package cohort mismatch. Expected exactly: $($expectedSymbolNames -join ', '). Found: $($symbolNames -join ', ')."
}

$legacySymbols = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File -Filter '*.symbols.nupkg')
if ($legacySymbols.Count -ne 0) {
    throw "Legacy .symbols.nupkg files are not permitted in the publication cohort: $($legacySymbols.Name -join ', ')."
}

$metadataById = @{}
foreach ($package in $runtimePackages) {
    $metadata = Read-NuspecMetadata -PackagePath $package.FullName
    if ($metadata.Id -notin $expectedPackageIds) {
        throw "Unexpected SharpAccess package ID '$($metadata.Id)' in '$($package.Name)'."
    }
    if ($metadata.Version -cne $Version) {
        throw "Package '$($metadata.Id)' has version '$($metadata.Version)', expected '$Version'."
    }
    if ($metadata.RepositoryUrl.TrimEnd('/') -cne $ExpectedRepositoryUrl.TrimEnd('/')) {
        throw "Package '$($metadata.Id)' repository URL '$($metadata.RepositoryUrl)' does not match '$ExpectedRepositoryUrl'."
    }
    if ($metadata.RepositoryCommit.ToLowerInvariant() -cne $ReleaseSha) {
        throw "Package '$($metadata.Id)' repository commit '$($metadata.RepositoryCommit)' does not match '$ReleaseSha'."
    }
    if ($metadata.LicenseExpression -cne $expectedLicenseExpression) {
        throw "Package '$($metadata.Id)' license expression '$($metadata.LicenseExpression)' does not match tracked package license '$expectedLicenseExpression'."
    }
    if ([string]::IsNullOrWhiteSpace($metadata.Readme)) {
        throw "Package '$($metadata.Id)' does not declare a package README."
    }

    foreach ($dependency in $metadata.Dependencies) {
        if ($dependency.Id -in @(('SharpAccess.Sql' + 'Server'), ('SharpAccess.My' + 'Sql'))) {
            throw "Package '$($metadata.Id)' contains forbidden dependency '$($dependency.Id)'."
        }
    }

    if ($metadata.Id -in @('SharpAccess.Sqlite', 'SharpAccess.Postgres')) {
        $coreDependencies = @($metadata.Dependencies | Where-Object Id -CEQ 'SharpAccess.Core')
        if ($coreDependencies.Count -eq 0) {
            throw "Provider package '$($metadata.Id)' does not depend on SharpAccess.Core."
        }
        if (-not ($coreDependencies | Where-Object { $_.Version -like "*$Version*" })) {
            throw "Provider package '$($metadata.Id)' does not reference the release Core version '$Version'."
        }
    }

    $metadataById[$metadata.Id] = $metadata
}

foreach ($symbolPackage in $symbolPackages) {
    $symbolMetadata = Read-NuspecMetadata -PackagePath $symbolPackage.FullName
    $expectedId = ($expectedPackageIds | Where-Object { $symbolPackage.Name -ceq "$($_).$Version.snupkg" } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($expectedId)) {
        throw "Could not map symbol package '$($symbolPackage.Name)' to the expected cohort."
    }
    if ($symbolMetadata.Id -cne $expectedId) {
        throw "Symbol package '$($symbolPackage.Name)' contains ID '$($symbolMetadata.Id)', expected '$expectedId'."
    }
    if ($symbolMetadata.Version -cne $Version) {
        throw "Symbol package '$($symbolPackage.Name)' has version '$($symbolMetadata.Version)', expected '$Version'."
    }
    if ('SymbolsPackage' -cnotin @($symbolMetadata.PackageTypes)) {
        throw "Symbol package '$($symbolPackage.Name)' does not declare NuGet package type 'SymbolsPackage'."
    }
}

$evidenceIndexPath = Resolve-ContainedPath -Root $resolvedArtifactRoot -RelativePath $EvidenceIndexRelativePath -Description 'Evidence index'
if (-not (Test-Path -LiteralPath $evidenceIndexPath -PathType Leaf)) {
    throw "Evidence index does not exist: $evidenceIndexPath"
}
if ([System.IO.Path]::GetExtension($evidenceIndexPath) -ine '.json') {
    throw 'Evidence index must be JSON.'
}
$evidenceIndex = Get-Content -LiteralPath $evidenceIndexPath -Raw | ConvertFrom-Json -Depth 100

$revisionMatches = @(Get-JsonMatches -Value $evidenceIndex -PropertyNamePattern '(?i)(revision|sha|commit)' -ExpectedValue $ReleaseSha)
if ($revisionMatches.Count -eq 0) {
    throw "Evidence index does not bind a revision/SHA/commit field to '$ReleaseSha'."
}
$versionMatches = @(Get-JsonMatches -Value $evidenceIndex -PropertyNamePattern '(?i)version' -ExpectedValue $Version)
if ($versionMatches.Count -eq 0) {
    throw "Evidence index does not bind a version field to '$Version'."
}
$passingStatusValues = @('passed', 'success', 'complete', 'completed')
$rootStatusProperties = @($evidenceIndex.PSObject.Properties | Where-Object { $_.Name -match '^(?i:status|conclusion|result)$' })
if ($rootStatusProperties.Count -gt 0) {
    foreach ($rootStatusProperty in $rootStatusProperties) {
        $rootStatus = ([string]$rootStatusProperty.Value).Trim().ToLowerInvariant()
        if ($rootStatus -notin $passingStatusValues) {
            throw "Evidence index root $($rootStatusProperty.Name) is '$($rootStatusProperty.Value)', not a passing value."
        }
    }
}
else {
    $passingStatusMatches = 0
    foreach ($passingStatus in $passingStatusValues) {
        $passingStatusMatches += @(Get-JsonMatches -Value $evidenceIndex -PropertyNamePattern '(?i)status|conclusion|result' -ExpectedValue $passingStatus).Count
    }
    if ($passingStatusMatches -eq 0) {
        throw 'Evidence index contains no passing/successful/completed status value.'
    }
}
foreach ($packageId in $expectedPackageIds) {
    if (-not (Test-JsonContainsScalar -Value $evidenceIndex -ExpectedValue $packageId)) {
        throw "Evidence index does not contain package ID '$packageId'."
    }
}

$checksumsPath = Resolve-ContainedPath -Root $resolvedArtifactRoot -RelativePath $ChecksumsRelativePath -Description 'Checksum manifest'
if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "Checksum manifest does not exist: $checksumsPath"
}
$checksums = Read-ChecksumManifest -Path $checksumsPath
foreach ($expectedName in $expectedAllNames) {
    if (-not $checksums.ContainsKey($expectedName)) {
        throw "Checksum manifest has no SHA-256 entry for '$expectedName'."
    }

    $files = @(Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File | Where-Object Name -CEQ $expectedName)
    if ($files.Count -ne 1) {
        throw "Expected exactly one '$expectedName' file, found $($files.Count)."
    }

    $actualHash = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $checksums[$expectedName]) {
        throw "SHA-256 mismatch for '$expectedName'. Expected '$($checksums[$expectedName])', got '$actualHash'."
    }
}

foreach ($packageId in $expectedPackageIds) {
    $runtimeName = "$packageId.$Version.nupkg"
    $runtimePath = (Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File | Where-Object Name -CEQ $runtimeName).FullName
    Write-WorkflowOutput -Name ($packageId.Replace('.', '_').ToLowerInvariant() + '_package') -Value $runtimePath
}

Write-Host 'NuGet publication artifact validation passed.'
Write-Host "Release commit: $ReleaseSha"
Write-Host "Version:        $Version"
Write-Host "Evidence index: $evidenceIndexPath"
Write-Host "Checksums:      $checksumsPath"
Write-Host "Packages:       $($expectedRuntimeNames -join ', ')"
Write-Host "Symbols:        $($expectedSymbolNames -join ', ')"
