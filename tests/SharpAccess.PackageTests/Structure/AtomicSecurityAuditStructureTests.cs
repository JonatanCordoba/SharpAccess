namespace SharpAccess.PackageTests;

public sealed class AtomicSecurityAuditStructureTests
{
    [Fact]
    public void ActiveProvidersKeepMandatorySecurityMutationsAndAuditRowsInOneTransaction()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string provider in new[] { "Sqlite", "Postgres" })
        {
            string storesRoot = Path.Combine(repositoryRoot, "providers", $"SharpAccess.{provider}", "Stores");
            string rotationSourcePath = Path.Combine(
                storesRoot,
                "Tokens",
                $"{provider}AuthStore.RefreshTokenRotation.cs");
            string auditSource = File.ReadAllText(Path.Combine(storesRoot, "Audit", $"{provider}AuthStore.Audit.cs"));
            string adminSource = File.ReadAllText(Path.Combine(storesRoot, "Users", $"{provider}AuthStore.AdminSeed.cs"));
            string oauthSource = File.ReadAllText(Path.Combine(storesRoot, "OAuth", $"{provider}AuthStore.OAuth.cs"));
            string refreshSource = ReadExistingSources(
                Path.Combine(storesRoot, "Tokens", $"{provider}AuthStore.RefreshTokens.cs"),
                rotationSourcePath,
                Path.Combine(storesRoot, "Tokens", $"{provider}AuthStore.RefreshTokenLimits.cs"));
            string mutationSource = ReadExistingSources(
                Path.Combine(storesRoot, "Users", $"{provider}AuthStore.Users.cs"),
                Path.Combine(storesRoot, "Users", $"{provider}AuthStore.AdminSeed.cs"),
                Path.Combine(storesRoot, "OAuth", $"{provider}AuthStore.OAuth.cs"),
                Path.Combine(storesRoot, "Tokens", $"{provider}AuthStore.OneTimeTokens.cs"),
                Path.Combine(storesRoot, "Tokens", $"{provider}AuthStore.RefreshTokens.cs"),
                rotationSourcePath,
                Path.Combine(storesRoot, "Tokens", $"{provider}AuthStore.RefreshTokenLimits.cs"),
                Path.Combine(storesRoot, "Authorization", $"{provider}AuthStore.Authorization.cs"),
                Path.Combine(storesRoot, "Tenants", $"{provider}AuthStore.Tenants.cs"));

            Assert.Contains("InsertAuditAsync(", auditSource, StringComparison.Ordinal);
            Assert.Contains("InsertAuditAsync(connection, null, audit", auditSource, StringComparison.Ordinal);
            Assert.Contains("AuditRecord audit", mutationSource, StringComparison.Ordinal);
            Assert.Contains("RefreshTokenAuditEvidence audit", mutationSource, StringComparison.Ordinal);
            Assert.Contains("HandleRefreshTokenReplayAsync", mutationSource, StringComparison.Ordinal);
            Assert.Contains("administrator_seeded", mutationSource, StringComparison.Ordinal);
            Assert.Contains("oauth_account_linked", mutationSource, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteAuditAsync(", mutationSource, StringComparison.Ordinal);
            Assert.Contains("InsertAuditAsync(connection, transaction", SliceFrom(adminSource, "AuditRecord audit"), StringComparison.Ordinal);

            string replayBlock = SliceBetween(refreshSource, "public async Task<bool> HandleRefreshTokenReplayAsync", "// Rotates");
            Assert.Contains("RevokedUtc.HasValue", replayBlock, StringComparison.Ordinal);
            Assert.Contains("InsertAuditAsync(connection, transaction", replayBlock, StringComparison.Ordinal);

            string oauthBindingBlock = SliceFrom(oauthSource, "AuditRecord audit");
            Assert.Contains("auditWriteStarted = true", oauthBindingBlock, StringComparison.Ordinal);
            Assert.Contains("InsertAuditAsync(connection, transaction", oauthBindingBlock, StringComparison.Ordinal);
            Assert.Contains("!auditWriteStarted", oauthBindingBlock, StringComparison.Ordinal);

            foreach (string status in new[] { "Reused", "Expired", "UserInvalid", "LimitExceeded", "Success" })
            {
                Assert.Contains($"TokenRotationStatus.{status}", refreshSource, StringComparison.Ordinal);
            }

            if (File.Exists(rotationSourcePath))
            {
                string rotationSource = File.ReadAllText(rotationSourcePath);
                if (provider == "Sqlite")
                {
                    Assert.Contains("RotateRefreshTokenCoreAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("InsertRotationAuditAsync(", rotationSource, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("GetRefreshRotationRejectionAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("CompleteRejectedRefreshRotationAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("CommitRefreshRotationOutcomeAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("RevokeFamilyInternalAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("RevokeSelectedRefreshTokenAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("InsertAuditAsync(", rotationSource, StringComparison.Ordinal);
                    Assert.Contains("transaction.CommitAsync(", rotationSource, StringComparison.Ordinal);
                }
            }

            string compactRefreshSource = Compact(refreshSource);
            Assert.Contains("InsertAuditAsync(connection,transaction", compactRefreshSource, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteAuditAsync(", compactRefreshSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CanonicalSecurityMutationEventsHaveNoPostCommitServiceWrites()
    {
        string servicesRoot = Path.Combine(FindRepositoryRoot(), "src", "SharpAccess.Core", "Services");
        foreach (string service in new[]
        {
            "AdministrationService.cs",
            "TenantService.cs",
            "PasswordChangeUseCase.cs",
            "RefreshSessionUseCase.cs"
        })
        {
            string source = File.ReadAllText(Path.Combine(servicesRoot, service));
            Assert.DoesNotContain("IAuditService", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteAuditAsync(", source, StringComparison.Ordinal);
        }

        string completionServices = ReadExistingSources(
            Path.Combine(servicesRoot, "PasswordResetUseCase.cs"),
            Path.Combine(servicesRoot, "EmailVerificationUseCase.cs"));
        string compact = Compact(completionServices);
        foreach (string eventType in new[] { "password_reset_completed", "email_verified" })
        {
            Assert.DoesNotContain($"WriteAsync(\"{eventType}\"", compact, StringComparison.Ordinal);
        }

        string refreshSource = File.ReadAllText(Path.Combine(servicesRoot, "RefreshSessionUseCase.cs"));
        string refreshMethod = SliceBetween(
            refreshSource,
            "public async Task<ServiceResult<SessionTokens>> RefreshAsync",
            "// Revokes the caller's selected refresh token");
        int replayDispatch = refreshMethod.IndexOf("HandleReplayAsync(", StringComparison.Ordinal);
        int userPreflight = refreshMethod.IndexOf("FindUserByIdAsync", StringComparison.Ordinal);
        Assert.True(replayDispatch >= 0 && replayDispatch < userPreflight);

        string replayHelper = SliceBetween(
            refreshSource,
            "private async Task<ServiceResult<SessionTokens>> HandleReplayAsync",
            "private async Task<bool> HasTenantAccessAsync");
        Assert.Contains("store.HandleRefreshTokenReplayAsync(", replayHelper, StringComparison.Ordinal);
        Assert.Contains("return InvalidRefreshToken();", replayHelper, StringComparison.Ordinal);
    }

    [Fact]
    public void StandaloneAuditObservationsUseTheExplicitBestEffortBoundary()
    {
        string repositoryRoot = FindRepositoryRoot();
        string servicesRoot = Path.Combine(repositoryRoot, "src", "SharpAccess.Core", "Services");
        string source = ReadExistingSources(
            Path.Combine(servicesRoot, "PasswordLoginUseCase.cs"),
            Path.Combine(servicesRoot, "RegistrationUseCase.cs"),
            Path.Combine(servicesRoot, "PasswordResetUseCase.cs"),
            Path.Combine(servicesRoot, "EmailVerificationUseCase.cs"),
            Path.Combine(repositoryRoot, "src", "SharpAccess.Core", "OAuth", "OpenIdConnectOAuthService.cs"));

        foreach (string eventType in new[]
        {
            "login_success",
            "login_failed",
            "password_reset_requested",
            "email_verification_requested",
            "oauth_login_success",
            "oauth_login_failed"
        })
        {
            Assert.Contains($"\"{eventType}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("TryWriteObservationAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("audit.WriteAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_audit.WriteAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizationFallbackEvidenceUsesCanonicalEventNames()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SharpAccess.Core",
            "Persistence",
            "IAuthAuthorizationStore.cs"));

        foreach (string canonical in new[]
        {
            "permission_changed",
            "role_assigned",
            "role_removed",
            "tenant_role_assigned",
            "tenant_role_removed"
        })
        {
            Assert.Contains($"\"{canonical}\"", source, StringComparison.Ordinal);
        }

        foreach (string legacy in new[]
        {
            "global_permission_changed",
            "global_role_assignment_changed",
            "tenant_role_assignment_changed"
        })
        {
            Assert.DoesNotContain(legacy, source, StringComparison.Ordinal);
        }
    }

    private static string ReadExistingSources(params string[] paths) =>
        string.Join(Environment.NewLine, paths.Where(File.Exists).Select(File.ReadAllText));

    private static string Compact(string source) =>
        string.Concat(source.Where(static character => !char.IsWhiteSpace(character)));

    private static string SliceFrom(string source, string marker)
    {
        int index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Required source marker was not found: {marker}");
        return source[index..];
    }

    private static string SliceBetween(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Required source marker was not found: {startMarker}");
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Required source marker was not found after {startMarker}: {endMarker}");
        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpAccess.sln"))) { return current.FullName; }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the SharpAccess repository root.");
    }
}
