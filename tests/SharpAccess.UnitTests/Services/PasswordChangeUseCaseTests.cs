using SharpAccess.Abstractions;
using SharpAccess.Configuration;
using SharpAccess.Domain;
using SharpAccess.Persistence;
using SharpAccess.Security;
using SharpAccess.Services;
using Microsoft.Extensions.Options;

namespace SharpAccess.UnitTests;

public sealed class PasswordChangeUseCaseTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly RequestMetadata Metadata =
        new("192.0.2.20", "password-change-coverage");
    private const string CurrentPassword = "CurrentPassword123!";
    private const string NewPassword = "NewPassword456!";

    [Theory]
    [InlineData("disabled")]
    [InlineData("empty-user")]
    [InlineData("invalid-new")]
    [InlineData("missing-current")]
    [InlineData("oversized-current")]
    public async Task InvalidInputsReturnInvalidInputBeforeLoadingTheUser(string scenario)
    {
        AuthOptions options = TestOptions.Create();
        Guid userId = UserId;
        string? currentPassword = CurrentPassword;
        string? newPassword = NewPassword;

        switch (scenario)
        {
            case "disabled":
                options.Features.PasswordAuthentication = false;
                break;
            case "empty-user":
                userId = Guid.Empty;
                break;
            case "invalid-new":
                newPassword = "short";
                break;
            case "missing-current":
                currentPassword = null;
                break;
            case "oversized-current":
                currentPassword = new string('x', options.Passwords.MaximumLength + 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        FakeUserStore store = new();
        PasswordChangeUseCase useCase = CreateUseCase(
            options,
            store,
            new FakePasswordHasher(),
            new RecordingAuditService());

        ServiceResult<bool> result = await useCase.ChangePasswordAsync(
            userId,
            currentPassword,
            newPassword,
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidInput, result.Error);
        Assert.Equal("invalid_password_change", result.Code);
        Assert.Equal(0, store.FindUserCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("unverified")]
    [InlineData("missing-hash")]
    public async Task InvalidPersistedUserStatesReturnUnauthorized(string scenario)
    {
        FakeUserStore store = new()
        {
            User = scenario switch
            {
                "missing" => null,
                "inactive" => User(isActive: false),
                "unverified" => User(emailVerified: false),
                "missing-hash" => User(passwordHash: null),
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            }
        };
        FakePasswordHasher hasher = new();
        PasswordChangeUseCase useCase = CreateUseCase(
            TestOptions.Create(),
            store,
            hasher,
            new RecordingAuditService());

        ServiceResult<bool> result = await useCase.ChangePasswordAsync(
            UserId,
            CurrentPassword,
            NewPassword,
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal("invalid_password_change", result.Code);
        Assert.Equal(1, store.FindUserCalls);
        Assert.Equal(0, hasher.VerifyCalls);
    }

    [Fact]
    public async Task FailedCurrentPasswordVerificationReturnsUnauthorized()
    {
        FakeUserStore store = new() { User = User() };
        FakePasswordHasher hasher = new()
        {
            VerificationStatus = PasswordVerificationStatus.Failed
        };
        RecordingAuditService audit = new();
        PasswordChangeUseCase useCase = CreateUseCase(
            TestOptions.Create(),
            store,
            hasher,
            audit);

        ServiceResult<bool> result = await useCase.ChangePasswordAsync(
            UserId,
            CurrentPassword,
            NewPassword,
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(0, hasher.HashCalls);
        Assert.Equal(0, store.ChangePasswordCalls);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task FailedPasswordPersistenceReturnsUnauthorizedWithoutAudit()
    {
        FakeUserStore store = new()
        {
            User = User(),
            ChangePasswordResult = false
        };
        FakePasswordHasher hasher = new()
        {
            VerificationStatus = PasswordVerificationStatus.Success,
            EncodedHash = "replacement-hash"
        };
        RecordingAuditService audit = new();
        PasswordChangeUseCase useCase = CreateUseCase(
            TestOptions.Create(),
            store,
            hasher,
            audit);

        ServiceResult<bool> result = await useCase.ChangePasswordAsync(
            UserId,
            CurrentPassword,
            NewPassword,
            Metadata);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.Unauthorized, result.Error);
        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(1, hasher.HashCalls);
        Assert.Equal(1, store.ChangePasswordCalls);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task SuccessfulPasswordChangePersistsAndAuditsTheMutation()
    {
        FakeUserStore store = new()
        {
            User = User(),
            ChangePasswordResult = true
        };
        FakePasswordHasher hasher = new()
        {
            VerificationStatus = PasswordVerificationStatus.SuccessNeedsRehash,
            EncodedHash = "replacement-hash"
        };
        RecordingAuditService audit = new();
        PasswordChangeUseCase useCase = CreateUseCase(
            TestOptions.Create(),
            store,
            hasher,
            audit);

        ServiceResult<bool> result = await useCase.ChangePasswordAsync(
            UserId,
            CurrentPassword,
            NewPassword,
            Metadata);

        Assert.True(result.Succeeded);
        Assert.True(result.Value);
        Assert.Equal(CurrentPassword, hasher.VerifiedPassword);
        Assert.Equal("existing-hash", hasher.VerifiedHash);
        Assert.Equal(NewPassword, hasher.HashedPassword);
        Assert.Equal(UserId, store.ChangedUserId);
        Assert.Equal("replacement-hash", store.ChangedPasswordHash);
        Assert.Equal(Now, store.ChangedUtc);

        AuditRecord recorded = Assert.IsType<AuditRecord>(store.AuditEvidence);
        Assert.Equal("password_changed", recorded.EventType);
        Assert.Equal(UserId, recorded.UserId);
        Assert.Null(recorded.TenantId);
        Assert.Equal(Metadata.IpAddress, recorded.IpAddress);
        Assert.Equal(Metadata.UserAgent, recorded.UserAgent);
        Assert.Null(recorded.Detail);
    }

    private static PasswordChangeUseCase CreateUseCase(
        AuthOptions options,
        FakeUserStore store,
        FakePasswordHasher hasher,
        RecordingAuditService audit) =>
        new(
            store,
            hasher,
            new InputValidator(Options.Create(options)),
            new FixedClock(),
            Options.Create(options));

    private static AuthUser User(
        bool isActive = true,
        bool emailVerified = true,
        string? passwordHash = "existing-hash") =>
        new(
            UserId,
            "person@example.com",
            "PERSON@EXAMPLE.COM",
            passwordHash,
            emailVerified ? Now : null,
            isActive,
            0,
            null,
            1,
            Now,
            Now);

    private sealed class FakeUserStore : IAuthUserStore
    {
        public AuthUser? User { get; init; }
        public bool ChangePasswordResult { get; init; }
        public int FindUserCalls { get; private set; }
        public int ChangePasswordCalls { get; private set; }
        public Guid ChangedUserId { get; private set; }
        public string? ChangedPasswordHash { get; private set; }
        public DateTimeOffset ChangedUtc { get; private set; }
        public AuditRecord? AuditEvidence { get; private set; }

        public Task<AuthUser?> FindUserByIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            FindUserCalls++;
            return Task.FromResult(User);
        }

        public Task<bool> ChangePasswordAsync(
            Guid userId,
            string passwordHash,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            ChangePasswordCalls++;
            ChangedUserId = userId;
            ChangedPasswordHash = passwordHash;
            ChangedUtc = now;
            return Task.FromResult(ChangePasswordResult);
        }

        // Captures atomic password-change audit evidence before delegating to the fake mutation.
        public Task<bool> ChangePasswordAsync(Guid userId, string passwordHash, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default)
        {
            AuditEvidence = audit;
            return ChangePasswordAsync(userId, passwordHash, now, cancellationToken);
        }

        public Task<bool> CreateUserWithVerificationTokenAsync(
            AuthUser user,
            string verificationTokenHash,
            DateTimeOffset verificationExpiresUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthUser?> FindUserByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthPageSlice<AuthUser>> ListUsersAsync(
            AuthPageQuery page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordLoginFailureAsync(
            Guid userId,
            int failureThreshold,
            DateTimeOffset lockoutEndUtc,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetLoginFailuresAsync(
            Guid userId,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> UpdatePasswordHashAsync(
            Guid userId,
            string expectedPasswordHash,
            int expectedSecurityVersion,
            string passwordHash,
            DateTimeOffset updatedUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SetUserActiveAsync(
            Guid userId,
            bool isActive,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        // Delegates the evidence-bearing status mutation for this password-change test double.
        public Task<bool> SetUserActiveAsync(Guid userId, bool isActive, DateTimeOffset now, AuditRecord audit, CancellationToken cancellationToken = default) =>
            SetUserActiveAsync(userId, isActive, now, cancellationToken);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public PasswordVerificationStatus VerificationStatus { get; init; } =
            PasswordVerificationStatus.Success;
        public string EncodedHash { get; init; } = "replacement-hash";
        public int VerifyCalls { get; private set; }
        public int HashCalls { get; private set; }
        public string? VerifiedPassword { get; private set; }
        public string? VerifiedHash { get; private set; }
        public string? HashedPassword { get; private set; }

        public Task<string> HashAsync(
            string password,
            CancellationToken cancellationToken = default)
        {
            HashCalls++;
            HashedPassword = password;
            return Task.FromResult(EncodedHash);
        }

        public Task<PasswordVerificationStatus> VerifyAsync(
            string password,
            string encodedHash,
            CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            VerifiedPassword = password;
            VerifiedHash = encodedHash;
            return Task.FromResult(VerificationStatus);
        }
    }

    private sealed class RecordingAuditService : IAuditService
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
            Events.Add(new AuditEvent(
                eventType,
                userId,
                tenantId,
                ipAddress,
                userAgent,
                detail));
            return Task.CompletedTask;
        }
    }

    private sealed record AuditEvent(
        string EventType,
        Guid? UserId,
        Guid? TenantId,
        string? IpAddress,
        string? UserAgent,
        string? Detail);

    private sealed class FixedClock : IAuthClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
