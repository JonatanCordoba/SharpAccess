#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateRange(1, 65535)][int]$Port = $(if ($env:APP_PORT) { [int]$env:APP_PORT } else { 5000 }),
    [string]$BaseUrl,
    [string]$TestEmail = $(if ($env:AUTH_TEST_EMAIL) { $env:AUTH_TEST_EMAIL } else { "admin@test.local" }),
    [string]$TestPassword = $(if ($env:AUTH_TEST_PASSWORD) { $env:AUTH_TEST_PASSWORD } else { "Admin123!Sample" }),
    [switch]$StartApi,
    [switch]$StopApi
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-RepositoryRoot([string]$Candidate) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        $scriptCandidate = $PSScriptRoot
        if (-not (Test-Path -LiteralPath (Join-Path $scriptCandidate "SharpAccess.sln") -PathType Leaf)) {
            $scriptCandidate = Join-Path $PSScriptRoot ".."
        }
        $Candidate = $scriptCandidate
    }

    $resolved = (Resolve-Path -LiteralPath $Candidate).Path
    if (-not (Test-Path -LiteralPath (Join-Path $resolved "SharpAccess.sln") -PathType Leaf)) {
        throw "Repository root is invalid; SharpAccess.sln was not found."
    }
    $sampleProject = Join-Path $resolved "samples/SharpAccess.SampleApi/SharpAccess.SampleApi.csproj"
    if (-not (Test-Path -LiteralPath $sampleProject -PathType Leaf)) {
        throw "Repository root is invalid; the SharpAccess sample API project was not found."
    }
    return $resolved
}

function Assert-Status([Microsoft.PowerShell.Commands.WebResponseObject]$Response, [int]$Expected, [string]$Context) {
    if ([int]$Response.StatusCode -ne $Expected) {
        throw "$Context returned HTTP $($Response.StatusCode); expected $Expected."
    }
}

function Invoke-CheckedJson([string]$Uri, [string]$Method, [object]$Body, [hashtable]$Headers = @{}) {
    $json = $Body | ConvertTo-Json -Compress -Depth 8
    return Invoke-WebRequest -Uri $Uri -Method $Method -Headers $Headers -ContentType "application/json" -Body $json -SkipHttpErrorCheck
}

$root = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($TestEmail) -or [string]::IsNullOrWhiteSpace($TestPassword)) {
    throw "Test email and password must be non-empty."
}
if ($StopApi -and -not $StartApi) {
    throw "-StopApi can only stop a process started by this invocation; add -StartApi."
}
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $BaseUrl = "http://127.0.0.1:$Port"
}
$BaseUrl = $BaseUrl.TrimEnd("/")
$parsedBaseUrl = [Uri]$BaseUrl
if (-not $parsedBaseUrl.IsAbsoluteUri -or $parsedBaseUrl.Scheme -notin @("http", "https") -or
    $parsedBaseUrl.Port -ne $Port -or $parsedBaseUrl.AbsolutePath -ne "/" -or
    -not [string]::IsNullOrEmpty($parsedBaseUrl.UserInfo) -or
    -not [string]::IsNullOrEmpty($parsedBaseUrl.Query) -or
    -not [string]::IsNullOrEmpty($parsedBaseUrl.Fragment)) {
    throw "BaseUrl must be an HTTP(S) origin without credentials, query, fragment, or path and must use port $Port."
}

$process = $null
$database = Join-Path $root "artifacts/check-api.db"
$logFile = Join-Path $root "artifacts/check-api.log"
$sampleProjectDirectory = Join-Path $root "samples/SharpAccess.SampleApi"

try {
    if ($StartApi) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $database) | Out-Null
        @($database, "$database-shm", "$database-wal", $logFile) | ForEach-Object {
            Remove-Item -LiteralPath $_ -Force -ErrorAction SilentlyContinue
        }

        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = "dotnet"
        $startInfo.WorkingDirectory = $sampleProjectDirectory
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $false
        $startInfo.RedirectStandardError = $false
        @(
            "run", "--project", "SharpAccess.SampleApi.csproj",
            "--configuration", "Release", "--no-launch-profile"
        ) | ForEach-Object { $startInfo.ArgumentList.Add($_) }
        $startInfo.Environment["APP_ENV"] = "Test"
        $startInfo.Environment["APP_PORT"] = [string]$Port
        $startInfo.Environment["APP_BASE_URL"] = $BaseUrl
        $startInfo.Environment["APP_JWT_KEY"] = "TEST-ONLY-JWT-SIGNING-KEY-12345678901234567890"
        $startInfo.Environment["APP_REFRESH_TOKEN_HASH_KEY"] = "TEST-ONLY-TOKEN-HASHING-KEY-12345678901234567890"
        $startInfo.Environment["APP_PASSWORD_PEPPER"] = "TEST-ONLY-PASSWORD-PEPPER-12345678901234567890"
        $startInfo.Environment["AUTH_CONNECTION_STRING"] = "Data Source=$database"
        $startInfo.Environment["APP_SEED_ADMIN"] = "true"
        $startInfo.Environment["APP_SEED_ADMIN_EMAIL"] = $TestEmail
        $startInfo.Environment["APP_SEED_ADMIN_PASSWORD"] = $TestPassword
        $startInfo.Environment["Auth__ReturnRefreshTokenInResponseBody"] = "true"
        $process = [System.Diagnostics.Process]::Start($startInfo)
    }

    $ready = $false
    $index = $null
    foreach ($attempt in 1..60) {
        if ($process -and $process.HasExited) {
            throw "The sample API exited before becoming ready."
        }

        try {
            $health = Invoke-WebRequest -Uri "$BaseUrl/health" -SkipHttpErrorCheck
            if ([int]$health.StatusCode -eq 200) {
                $candidateIndex = Invoke-WebRequest -Uri "$BaseUrl/" -SkipHttpErrorCheck
                if ([int]$candidateIndex.StatusCode -eq 200 -and $candidateIndex.Content -match "SharpAccess") {
                    $index = $candidateIndex
                    $ready = $true
                    break
                }
            }
        }
        catch { }

        if ($process -and $process.HasExited) {
            throw "The sample API exited before becoming ready."
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw "The sample API health endpoint and static console did not become ready at the selected base URL."
    }

    Assert-Status $index 200 "Static console"

    $malformed = Invoke-WebRequest -Uri "$BaseUrl/auth/login" -Method Post -ContentType "application/json" -Body "{" -SkipHttpErrorCheck
    Assert-Status $malformed 400 "Malformed JSON"
    if ([string]$malformed.Headers["Content-Type"] -notmatch "application/problem\+json") {
        throw "Malformed JSON did not return ProblemDetails."
    }
    if ($malformed.Content -match "exception|stack trace| at [A-Za-z0-9_.]+\(|\.cs:[0-9]+") {
        throw "Malformed response leaked implementation details."
    }

    $anonymous = Invoke-WebRequest -Uri "$BaseUrl/admin/users" -SkipHttpErrorCheck
    Assert-Status $anonymous 401 "Anonymous administration request"

    $knownInvalid = Invoke-CheckedJson "$BaseUrl/auth/login" Post @{
        email = $TestEmail; password = "definitely-not-the-password"; tenantId = $null
    }
    $unknownInvalid = Invoke-CheckedJson "$BaseUrl/auth/login" Post @{
        email = "missing-user@example.invalid"; password = "definitely-not-the-password"; tenantId = $null
    }
    Assert-Status $knownInvalid 401 "Known account with invalid credentials"
    Assert-Status $unknownInvalid 401 "Unknown account with invalid credentials"
    $invalidText = "$($knownInvalid.Content) $($unknownInvalid.Content)"
    if ($invalidText -match "exist|unknown|inactive|disabled|verified|locked|password") {
        throw "Invalid-credential response revealed account state."
    }

    $login = Invoke-CheckedJson "$BaseUrl/auth/login" Post @{
        email = $TestEmail; password = $TestPassword; tenantId = $null
    }
    Assert-Status $login 200 "Administrator login"
    $loginBody = $login.Content | ConvertFrom-Json
    if (-not $loginBody.accessToken -or -not $loginBody.refreshToken) {
        throw "Login did not return both tokens."
    }
    $setCookie = [string]::Join(";", @($login.Headers["Set-Cookie"]))
    if ($setCookie -notmatch "HttpOnly" -or $setCookie -notmatch "SameSite=Lax") {
        throw "Refresh cookie flags are incomplete."
    }
    $headers = @{ Authorization = "Bearer $($loginBody.accessToken)" }

    $me = Invoke-WebRequest -Uri "$BaseUrl/auth/me" -Headers $headers -SkipHttpErrorCheck
    Assert-Status $me 200 "Current profile"
    if ($me.Content -notmatch [Regex]::Escape($TestEmail)) {
        throw "Current profile did not return the seeded administrator."
    }
    $users = Invoke-WebRequest -Uri "$BaseUrl/admin/users" -Headers $headers -SkipHttpErrorCheck
    Assert-Status $users 200 "Administrator role/permission request"
    $adminDemo = Invoke-WebRequest -Uri "$BaseUrl/demo/admin" -Headers $headers -SkipHttpErrorCheck
    Assert-Status $adminDemo 200 "Combined Admin role and permission request"

    $rotated = Invoke-CheckedJson "$BaseUrl/auth/refresh" Post @{
        refreshToken = $loginBody.refreshToken; tenantId = $null
    }
    Assert-Status $rotated 200 "Refresh rotation"
    $rotatedBody = $rotated.Content | ConvertFrom-Json
    if (-not $rotatedBody.refreshToken -or $rotatedBody.refreshToken -eq $loginBody.refreshToken) {
        throw "Refresh token did not rotate."
    }

    $reuse = Invoke-CheckedJson "$BaseUrl/auth/refresh" Post @{
        refreshToken = $loginBody.refreshToken; tenantId = $null
    }
    Assert-Status $reuse 401 "Refresh reuse"
    $family = Invoke-CheckedJson "$BaseUrl/auth/refresh" Post @{
        refreshToken = $rotatedBody.refreshToken; tenantId = $null
    }
    Assert-Status $family 401 "Replacement after family revocation"

    Write-Host "API check passed at $BaseUrl."
}
finally {
    if ($StopApi -and $process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(5000) | Out-Null
    }
    if ($process) {
        $process.Dispose()
    }
}
