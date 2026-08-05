using SharpAccess.Diagnostics;
using SharpAccess.Domain;

namespace SharpAccess.UnitTests;

public sealed class DiagnosticOperationInvariantTests
{
    [Theory]
    [MemberData(nameof(RemainingOperations))]
    public async Task RemainingDiagnosticOperationsMapToStableNames(
        int operationValue)
    {
        SharpAccessDiagnosticOperation operation =
            (SharpAccessDiagnosticOperation)operationValue;

        ServiceResult<bool> result = await SharpAccessDiagnostics.TrackAsync(
            operation,
            static () => Task.FromResult(ServiceResult<bool>.Success(true)));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UndefinedDiagnosticOperationIsRejected()
    {
        SharpAccessDiagnosticOperation invalid =
            (SharpAccessDiagnosticOperation)int.MaxValue;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => SharpAccessDiagnostics.TrackAsync(
                invalid,
                static () => Task.FromResult(
                    ServiceResult<bool>.Success(true))));
    }

    public static TheoryData<int> RemainingOperations => new()
    {
        (int)SharpAccessDiagnosticOperation.Logout,
        (int)SharpAccessDiagnosticOperation.Revoke,
        (int)SharpAccessDiagnosticOperation.CurrentUser,
        (int)SharpAccessDiagnosticOperation.ChangePassword,
        (int)SharpAccessDiagnosticOperation.ForgotPassword,
        (int)SharpAccessDiagnosticOperation.ResetPassword,
        (int)SharpAccessDiagnosticOperation.VerifyEmail,
        (int)SharpAccessDiagnosticOperation.ResendVerification
    };
}
