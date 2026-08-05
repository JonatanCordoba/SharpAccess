@{
    RootModule = 'SharpAccess.Build.psm1'
    ModuleVersion = '0.1.0'
    GUID = '93e16f44-d626-4aa5-97c7-24258b30a661'
    Author = 'SharpAccess contributors'
    Description = 'Internal deterministic repository helpers for SharpAccess PowerShell automation.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Resolve-SharpAccessRepositoryRoot',
        'Resolve-SharpAccessRepositoryPath',
        'Get-SharpAccessVersion',
        'Get-SharpAccessRevision',
        'Invoke-SharpAccessDotNet',
        'Write-SharpAccessUtf8NoBom'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
