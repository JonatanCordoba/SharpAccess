using SharpAccess.Domain;
using SharpAccess.Security;
using SharpAccess.Services;

namespace SharpAccess.UnitTests;

public sealed class PasswordRiskAuthServiceTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset TokenIssuedUtc = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly RequestMetadata Metadata = new("127.0.0.1", "unit");

    [Fact]
    public async Task DefaultPasswordRiskValidatorRejectsBlankCommonAndAccountDerivedCandidates()
    {
        DefaultPasswordRiskValidator validator = new();

        Assert.False(await validator.IsAllowedAsync(" ", null));
        Assert.False(await validator.IsAllowedAsync("dotnetauth123", null));
        Assert.False(await validator.IsAllowedAsync("prefixPERSONsuffix123", "PERSON@example.com"));
        Assert.True(await validator.IsAllowedAsync("ReliableCandidate123!", "PERSON@example.com"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RegisterRejectsInvalidOrRiskyPasswordsBeforeDelegating(bool passwordValid, bool riskAllowed)
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: passwordValid),
            new FakePasswordRiskValidator(allowed: riskAllowed));

        ServiceResult<string> result = await service.RegisterAsync(
            "person@example.com",
            "ValidPassword123",
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal("invalid_registration", result.Code);
        Assert.Equal(0, useCases.RegisterCalls);
    }

    [Fact]
    public async Task RegisterDelegatesWhenPasswordPassesValidationAndRiskChecks()
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: true),
            new FakePasswordRiskValidator(allowed: true));

        ServiceResult<string> result = await service.RegisterAsync(
            "person@example.com",
            "ValidPassword123",
            Metadata);

        Assert.True(result.Succeeded);
        Assert.Equal("registered", result.Value);
        Assert.Equal(1, useCases.RegisterCalls);
    }

    [Theory]
    [InlineData(false, true, "invalid_password_change")]
    [InlineData(true, false, "invalid_password_change")]
    public async Task ChangePasswordRejectsInvalidOrRiskyPasswordsBeforeDelegating(
        bool passwordValid,
        bool riskAllowed,
        string expectedCode)
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: passwordValid),
            new FakePasswordRiskValidator(allowed: riskAllowed));

        ServiceResult<bool> result = await service.ChangePasswordAsync(
            UserId,
            "OldPassword123",
            "NewPassword123",
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, useCases.ChangePasswordCalls);
    }

    [Fact]
    public async Task ChangePasswordDelegatesWhenNewPasswordPassesValidationAndRiskChecks()
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: true),
            new FakePasswordRiskValidator(allowed: true));

        ServiceResult<bool> result = await service.ChangePasswordAsync(
            UserId,
            "OldPassword123",
            "NewPassword123",
            Metadata);

        Assert.True(result.Succeeded);
        Assert.True(result.Value);
        Assert.Equal(1, useCases.ChangePasswordCalls);
    }

    [Theory]
    [InlineData(false, true, "invalid_password_reset")]
    [InlineData(true, false, "invalid_password_reset")]
    public async Task ResetPasswordRejectsInvalidOrRiskyPasswordsBeforeDelegating(
        bool passwordValid,
        bool riskAllowed,
        string expectedCode)
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: passwordValid),
            new FakePasswordRiskValidator(allowed: riskAllowed));

        ServiceResult<bool> result = await service.ResetPasswordAsync(
            "token",
            "NewPassword123",
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(0, useCases.ResetPasswordCalls);
    }

    [Fact]
    public async Task ResetPasswordDelegatesWhenNewPasswordPassesValidationAndRiskChecks()
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: true),
            new FakePasswordRiskValidator(allowed: true));

        ServiceResult<bool> result = await service.ResetPasswordAsync(
            "token",
            "NewPassword123",
            Metadata);

        Assert.True(result.Succeeded);
        Assert.True(result.Value);
        Assert.Equal(1, useCases.ResetPasswordCalls);
    }

    [Fact]
    public async Task ReadOnlyAndSessionFlowsDelegateToFocusedUseCases()
    {
        FakeAuthUseCases useCases = new();
        PasswordRiskAuthService service = CreateService(
            useCases,
            new FakeInputValidator(emailValid: true, passwordValid: true),
            new FakePasswordRiskValidator(allowed: true));

        Assert.True((await service.LoginAsync("person@example.com", "Password123!", TenantId, Metadata)).Succeeded);
        Assert.True((await service.RefreshAsync("refresh", TenantId, Metadata)).Succeeded);
        Assert.True((await service.LogoutAsync(UserId, "refresh", Metadata)).Succeeded);
        Assert.True((await service.RevokeAsync(
            UserId,
            canManageSessions: true,
            refreshToken: "refresh",
            revokeFamily: true,
            metadata: Metadata)).Succeeded);
        Assert.True((await service.GetMeAsync(UserId, TenantId)).Succeeded);
        Assert.True((await service.ForgotPasswordAsync("person@example.com", Metadata)).Succeeded);
        Assert.True((await service.VerifyEmailAsync("token", Metadata)).Succeeded);
        Assert.True((await service.ResendVerificationAsync("person@example.com", Metadata)).Succeeded);

        Assert.Equal(1, useCases.LoginCalls);
        Assert.Equal(1, useCases.RefreshCalls);
        Assert.Equal(1, useCases.LogoutCalls);
        Assert.Equal(1, useCases.RevokeCalls);
        Assert.Equal(1, useCases.GetMeCalls);
        Assert.Equal(1, useCases.ForgotPasswordCalls);
        Assert.Equal(1, useCases.VerifyEmailCalls);
        Assert.Equal(1, useCases.ResendVerificationCalls);
    }

    private static PasswordRiskAuthService CreateService(
        FakeAuthUseCases useCases,
        IInputValidator inputValidator,
        IPasswordRiskValidator passwordRiskValidator) =>
        new(
            useCases,
            useCases,
            useCases,
            useCases,
            useCases,
            useCases,
            useCases,
            inputValidator,
            passwordRiskValidator);

    private sealed class FakeInputValidator(bool emailValid, bool passwordValid) : IInputValidator
    {
        public bool TryValidateEmail(string? email, out string normalizedEmail)
        {
            normalizedEmail = emailValid ? "PERSON@EXAMPLE.COM" : string.Empty;
            return emailValid;
        }

        public bool IsValidPassword(string? password) => passwordValid;

        public bool TryValidateName(string? name, int maximumLength, out string normalizedName)
        {
            normalizedName = name ?? string.Empty;
            return true;
        }

        public bool TryValidateSlug(string? slug, out string normalizedSlug)
        {
            normalizedSlug = slug ?? string.Empty;
            return true;
        }

        public bool TryValidateReturnUrl(string? returnUrl, out string safeReturnUrl)
        {
            safeReturnUrl = returnUrl ?? "/";
            return true;
        }
    }

    private sealed class FakePasswordRiskValidator(bool allowed) : IPasswordRiskValidator
    {
        public ValueTask<bool> IsAllowedAsync(
            string password,
            string? normalizedEmail = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(allowed);
    }

    private sealed class FakeAuthUseCases :
        IRegistrationUseCase,
        IPasswordLoginUseCase,
        IRefreshSessionUseCase,
        ICurrentUserUseCase,
        IPasswordChangeUseCase,
        IPasswordResetUseCase,
        IEmailVerificationUseCase
    {
        public int RegisterCalls { get; private set; }

        public int LoginCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public int LogoutCalls { get; private set; }

        public int RevokeCalls { get; private set; }

        public int GetMeCalls { get; private set; }

        public int ChangePasswordCalls { get; private set; }

        public int ForgotPasswordCalls { get; private set; }

        public int ResetPasswordCalls { get; private set; }

        public int VerifyEmailCalls { get; private set; }

        public int ResendVerificationCalls { get; private set; }

        public Task<ServiceResult<string>> RegisterAsync(
            string? email,
            string? password,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return Task.FromResult(ServiceResult<string>.Success("registered"));
        }

        public Task<ServiceResult<SessionTokens>> LoginAsync(
            string? email,
            string? password,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LoginCalls++;
            return Task.FromResult(ServiceResult<SessionTokens>.Success(Tokens()));
        }

        public Task<ServiceResult<SessionTokens>> RefreshAsync(
            string? refreshToken,
            Guid? tenantId,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(ServiceResult<SessionTokens>.Success(Tokens()));
        }

        public Task<ServiceResult<bool>> LogoutAsync(
            Guid userId,
            string? refreshToken,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            LogoutCalls++;
            return Task.FromResult(ServiceResult<bool>.Success(true));
        }

        public Task<ServiceResult<bool>> RevokeAsync(
            Guid userId,
            bool canManageSessions,
            string? refreshToken,
            bool revokeFamily,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            return Task.FromResult(ServiceResult<bool>.Success(true));
        }

        public Task<ServiceResult<UserContext>> GetMeAsync(
            Guid userId,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            GetMeCalls++;
            EffectiveAuthorizationContext authorization = new(
                new GlobalAuthorizationContext(["user"], ["auth.sessions.manage"]),
                tenantId.HasValue
                    ? new TenantAuthorizationContext(tenantId.Value, false, [], [])
                    : null,
                AuthorizationVersion: 1);
            return Task.FromResult(ServiceResult<UserContext>.Success(new UserContext(
                userId,
                "person@example.com",
                true,
                authorization,
                SecurityVersion: 1)));
        }

        public Task<ServiceResult<bool>> ChangePasswordAsync(
            Guid userId,
            string? currentPassword,
            string? newPassword,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ChangePasswordCalls++;
            return Task.FromResult(ServiceResult<bool>.Success(true));
        }

        public Task<ServiceResult<string>> ForgotPasswordAsync(
            string? email,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ForgotPasswordCalls++;
            return Task.FromResult(ServiceResult<string>.Success("forgot"));
        }

        public Task<ServiceResult<bool>> ResetPasswordAsync(
            string? token,
            string? newPassword,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ResetPasswordCalls++;
            return Task.FromResult(ServiceResult<bool>.Success(true));
        }

        public Task<ServiceResult<bool>> VerifyEmailAsync(
            string? token,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            VerifyEmailCalls++;
            return Task.FromResult(ServiceResult<bool>.Success(true));
        }

        public Task<ServiceResult<string>> ResendVerificationAsync(
            string? email,
            RequestMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ResendVerificationCalls++;
            return Task.FromResult(ServiceResult<string>.Success("resend"));
        }

        private static SessionTokens Tokens() => new(
            "access",
            TokenIssuedUtc.AddMinutes(15),
            "refresh",
            TokenIssuedUtc.AddDays(30));
    }
}
