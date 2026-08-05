using SharpAccess.Domain;

namespace SharpAccess.UnitTests;

public sealed class SecurityAuditEvidenceTests
{
    private static readonly DateTimeOffset CreatedUtc =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateBoundsAndTrimsEveryUntrustedTextField()
    {
        AuditRecord evidence = SecurityAuditEvidence.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CreatedUtc,
            $"  {new string('e', 140)}  ",
            null,
            null,
            $"  {new string('i', 70)}  ",
            $"  {new string('u', 520)}  ",
            $"  {new string('d', 1_040)}  ");

        Assert.Equal(128, evidence.EventType.Length);
        Assert.Equal(64, evidence.IpAddress?.Length);
        Assert.Equal(512, evidence.UserAgent?.Length);
        Assert.Equal(1_024, evidence.Detail?.Length);
        Assert.DoesNotContain(' ', evidence.EventType);
    }

    [Fact]
    public void CreateDoesNotLeaveAnUnpairedUtf16SurrogateAtTheBoundary()
    {
        AuditRecord evidence = SecurityAuditEvidence.Create(
            CreatedUtc,
            new string('e', 127) + "😀tail",
            null,
            null,
            null,
            null,
            null);

        Assert.Equal(127, evidence.EventType.Length);
        Assert.False(char.IsSurrogate(evidence.EventType[^1]));
    }

    [Fact]
    public void RotationBundleSelectsExactlyOneCanonicalOutcomeRecord()
    {
        RefreshTokenAuditEvidence evidence = SecurityAuditEvidence.ForRefreshRotation(
            CreatedUtc,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            familyDetail: "family=cccccccc-cccc-cccc-cccc-cccccccccccc");

        Assert.Equal("refresh_token_rotated", evidence.For(TokenRotationStatus.Success).EventType);
        Assert.Equal("refresh_token_reuse_detected", evidence.For(TokenRotationStatus.Reused).EventType);
        Assert.Equal("refresh_token_family_revoked", evidence.For(TokenRotationStatus.UserInvalid).EventType);
        Assert.Equal("refresh_token_expired", evidence.For(TokenRotationStatus.Expired).EventType);
        Assert.Equal("refresh_token_family_revoked", evidence.For(TokenRotationStatus.LimitExceeded).EventType);
        Assert.Contains("reason=user_invalid", evidence.UserInvalid.Detail, StringComparison.Ordinal);
        Assert.Contains("reason=family_limit", evidence.LimitExceeded.Detail, StringComparison.Ordinal);
        Assert.Equal(5, new[]
        {
            evidence.Rotated.Id,
            evidence.Reused.Id,
            evidence.UserInvalid.Id,
            evidence.Expired.Id,
            evidence.LimitExceeded.Id
        }.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => evidence.For(TokenRotationStatus.NotFound));
    }
}
