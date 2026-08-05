using Microsoft.Extensions.Options;
using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using Xunit;

namespace SharpAccess.UnitTests.Services;

public sealed class EmailVerificationUseCaseTests
{
    private const string GenericResponse = "If the account requires verification, a message has been sent.";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResendVerificationAsyncReturnsDisabledWhenRegistrationIsOff()
    {
        TestContext context = CreateContext(registrationEnabled: false, user: null, validEmail: true);

        ServiceResult<string> result = await context.UseCase.ResendVerificationAsync(
            "user@example.com",
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Disabled, result.Error);
        Assert.Equal("registration_disabled", result.Code);
        Assert.Equal(0, context.Store.FindCalls);
    }

    [Fact]
    public async Task ResendVerificationAsyncReturnsGenericSuccessForInvalidEmail()
    {
        TestContext context = CreateContext(registrationEnabled: true, user: null, validEmail: false);

        ServiceResult<string> result = await context.UseCase.ResendVerificationAsync(
            "not-an-email",
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.True(result.Succeeded);
        Assert.Equal(GenericResponse, result.Value);
        Assert.Equal(0, context.Store.FindCalls);
        Assert.Equal(0, context.Store.ReplaceCalls);
        Assert.Empty(context.EmailSender.Messages);
        Assert.Empty(context.Audit.Events);
    }

    [Theory]
    [InlineData(AccountState.Missing)]
    [InlineData(AccountState.Inactive)]
    [InlineData(AccountState.AlreadyVerified)]
    public async Task ResendVerificationAsyncDoesNotRevealIneligibleAccountState(AccountState state)
    {
        AuthUser? user = state switch
        {
            AccountState.Missing => null,
            AccountState.Inactive => CreateUser(isActive: false, emailVerifiedUtc: null),
            AccountState.AlreadyVerified => CreateUser(isActive: true, emailVerifiedUtc: Now.AddDays(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        TestContext context = CreateContext(registrationEnabled: true, user: user, validEmail: true);

        ServiceResult<string> result = await context.UseCase.ResendVerificationAsync(
            "user@example.com",
            new RequestMetadata("127.0.0.1", "unit-test"));

        Assert.True(result.Succeeded);
        Assert.Equal(GenericResponse, result.Value);
        Assert.Equal(1, context.Store.FindCalls);
        Assert.Equal(0, context.Store.ReplaceCalls);
        Assert.Empty(context.EmailSender.Messages);
        Assert.Empty(context.Audit.Events);
    }

    [Fact]
    public async Task ResendVerificationAsyncReplacesTokenSendsMessageAndWritesAuditForEligibleAccount()
    {
        AuthUser user = CreateUser(isActive: true, emailVerifiedUtc: null);
        TestContext context = CreateContext(registrationEnabled: true, user: user, validEmail: true);
        RequestMetadata metadata = new("203.0.113.10", "SharpAccess tests");

        ServiceResult<string> result = await context.UseCase.ResendVerificationAsync(
            " user@example.com ",
            metadata);

        Assert.True(result.Succeeded);
        Assert.Equal(GenericResponse, result.Value);
        Assert.Equal(1, context.Store.FindCalls);
        Assert.Equal("USER@EXAMPLE.COM", context.Store.LastNormalizedEmail);
        Assert.Equal(1, context.Store.ReplaceCalls);
        Assert.Equal(user.Id, context.Store.ReplacedUserId);
        Assert.Equal("email_verification", context.Store.ReplacedPurpose);
        Assert.Equal("hash:raw-token", context.Store.ReplacedHash);
        Assert.Equal(Now, context.Store.ReplacedCreatedUtc);
        Assert.Equal(Now.AddMinutes(60), context.Store.ReplacedExpiresUtc);

        AuthEmailMessage message = Assert.Single(context.EmailSender.Messages);
        Assert.Equal(user.Email, message.Recipient);
        Assert.Equal("Verify your email address", message.Subject);
        Assert.Contains("verify_token=raw-token", message.TextBody, StringComparison.Ordinal);
        Assert.NotNull(message.HtmlBody);
        Assert.Contains("verify_token=raw-token", message.HtmlBody, StringComparison.Ordinal);

        AuditEvent audit = Assert.Single(context.Audit.Events);
        Assert.Equal("email_verification_requested", audit.EventType);
        Assert.Equal(user.Id, audit.UserId);
        Assert.Equal(metadata.IpAddress, audit.IpAddress);
        Assert.Equal(metadata.UserAgent, audit.UserAgent);
        Assert.Equal("resend", audit.Detail);
    }

    private static TestContext CreateContext(
        bool registrationEnabled,
        AuthUser? user,
        bool validEmail)
    {
        AuthOptions options = new()
        {
            BaseUri = new Uri("https://client.example.test/auth", UriKind.Absolute),
            EmailVerificationMinutes = 60,
            Features = new AuthFeatureOptions { Registration = registrationEnabled }
        };
        FakeStore store = new(user);
        FakeTokenProtector tokens = new();
        FakeInputValidator validator = new(validEmail);
        FakeEmailSender emailSender = new();
        FakeAuditService audit = new();
        EmailVerificationUseCase useCase = new(
            store,
            tokens,
            validator,
            emailSender,
            audit,
            new FakeClock(Now),
            Options.Create(options));
        return new TestContext(useCase, store, emailSender, audit);
    }

    private static AuthUser CreateUser(bool isActive, DateTimeOffset? emailVerifiedUtc)
    {
        return new AuthUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user@example.com",
            "USER@EXAMPLE.COM",
            "password-hash",
            emailVerifiedUtc,
            isActive,
            0,
            null,
            1,
            Now.AddDays(-30),
            Now.AddDays(-1));
    }

    public enum AccountState
    {
        Missing,
        Inactive,
        AlreadyVerified
    }

    private sealed record TestContext(
        EmailVerificationUseCase UseCase,
        FakeStore Store,
        FakeEmailSender EmailSender,
        FakeAuditService Audit);

    private sealed class FakeClock(DateTimeOffset now) : IAuthClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeTokenProtector : ITokenProtector
    {
        public string Generate(int byteLength = 48)
        {
            Assert.Equal(48, byteLength);
            return "raw-token";
        }

        public string Hash(string rawToken) => $"hash:{rawToken}";
    }

    private sealed class FakeInputValidator(bool validEmail) : IInputValidator
    {
        public bool TryValidateEmail(string? email, out string normalizedEmail)
        {
            normalizedEmail = validEmail ? "USER@EXAMPLE.COM" : string.Empty;
            return validEmail;
        }

        public bool IsValidPassword(string? password) => throw new NotSupportedException();

        public bool TryValidateName(string? name, int maximumLength, out string normalizedName) =>
            throw new NotSupportedException();

        public bool TryValidateSlug(string? slug, out string normalizedSlug) =>
            throw new NotSupportedException();

        public bool TryValidateReturnUrl(string? returnUrl, out string safeReturnUrl) =>
            throw new NotSupportedException();
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<AuthEmailMessage> Messages { get; } = [];

        public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEvent(
        string EventType,
        Guid? UserId,
        string? IpAddress,
        string? UserAgent,
        string? Detail);

    private sealed class FakeAuditService : IAuditService
    {
        public List<AuditEvent> Events { get; } = [];

        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Null(tenantId);
            Events.Add(new AuditEvent(eventType, userId, ipAddress, userAgent, detail));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(AuthUser? user) : IAuthUserOneTimeTokenStore
    {
        public int FindCalls { get; private set; }
        public int ReplaceCalls { get; private set; }
        public string? LastNormalizedEmail { get; private set; }
        public Guid? ReplacedUserId { get; private set; }
        public string? ReplacedPurpose { get; private set; }
        public string? ReplacedHash { get; private set; }
        public DateTimeOffset? ReplacedCreatedUtc { get; private set; }
        public DateTimeOffset? ReplacedExpiresUtc { get; private set; }

        public Task<AuthUser?> FindUserByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindCalls++;
            LastNormalizedEmail = normalizedEmail;
            return Task.FromResult(user);
        }

        public Task<bool> ReplaceOneTimeTokenAsync(
            Guid userId,
            string purpose,
            string tokenHash,
            DateTimeOffset createdUtc,
            DateTimeOffset expiresUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceCalls++;
            ReplacedUserId = userId;
            ReplacedPurpose = purpose;
            ReplacedHash = tokenHash;
            ReplacedCreatedUtc = createdUtc;
            ReplacedExpiresUtc = expiresUtc;
            return Task.FromResult(true);
        }

        public Task<bool> CreateUserWithVerificationTokenAsync(
            AuthUser createdUser,
            string verificationTokenHash,
            DateTimeOffset verificationExpiresUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthUser?> FindUserByIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthPageSlice<AuthUser>> ListUsersAsync(
            AuthPageQuery page,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordLoginFailureAsync(
            Guid userId,
            int failureThreshold,
            DateTimeOffset lockoutEndUtc,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ResetLoginFailuresAsync(
            Guid userId,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> UpdatePasswordHashAsync(
            Guid userId,
            string expectedPasswordHash,
            int expectedSecurityVersion,
            string passwordHash,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string passwordHash,
            DateTimeOffset now,
            AuditRecord audit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> SetUserActiveAsync(
            Guid userId,
            bool isActive,
            DateTimeOffset now,
            AuditRecord audit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid?> VerifyEmailAsync(
            string tokenHash,
            DateTimeOffset now,
            AuditRecord audit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid?> ResetPasswordAsync(
            string tokenHash,
            string passwordHash,
            DateTimeOffset now,
            AuditRecord audit,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> CreateOneTimeTokenAsync(
            Guid userId,
            string purpose,
            string tokenHash,
            DateTimeOffset createdUtc,
            DateTimeOffset expiresUtc,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OneTimeTokenRecord?> ConsumeOneTimeTokenAsync(
            string purpose,
            string tokenHash,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
