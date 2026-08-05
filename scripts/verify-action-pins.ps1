#Requires -Version 7.0
[CmdletBinding()] param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { (Resolve-Path (Join-Path $PSScriptRoot "..")).Path } else { (Resolve-Path $RepositoryRoot).Path }
[xml]$policy = Get-Content -LiteralPath (Join-Path $root "eng/SupplyChain.props") -Raw
$pins = @{}
foreach ($item in $policy.Project.ItemGroup.ActionPin) {
    if ([string]::IsNullOrWhiteSpace($item.Include) -or $item.Sha -cnotmatch '^[0-9a-f]{40}$') {
        throw "Invalid ActionPin in eng/SupplyChain.props: $($item.OuterXml)"
    }
    $pins[$item.Include] = $item.Sha
}
$seen = @{}
$failures = [System.Collections.Generic.List[string]]::new()
Get-ChildItem (Join-Path $root ".github/workflows") -File | ForEach-Object {
    $workflowPath = $_.FullName
    $lineNumber = 0
    Get-Content $workflowPath | ForEach-Object {
        $lineNumber++
        if ($_ -match '^\s*-?\s*uses:\s*([^#\s]+)') {
            $value = $Matches[1]
            if ($value.StartsWith('./')) { return }
            $separator = $value.LastIndexOf('@')
            if ($separator -le 0) { $failures.Add("${workflowPath}:$lineNumber action has no immutable reference: $value"); return }
            $action = $value.Substring(0, $separator)
            $reference = $value.Substring($separator + 1)
            if (-not $pins.ContainsKey($action)) { $failures.Add("${workflowPath}:$lineNumber action is absent from eng/SupplyChain.props: $action"); return }
            if ($reference -cne $pins[$action]) { $failures.Add("${workflowPath}:$lineNumber action differs from eng/SupplyChain.props: $value"); return }
            $seen[$action] = $true
        }
    }
}
foreach ($action in $pins.Keys) { if (-not $seen.ContainsKey($action)) { $failures.Add("Central action pin is unused by every workflow: $action") } }
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Host "All workflow actions match the centrally reviewed full commit SHAs."
