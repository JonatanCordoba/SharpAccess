using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharpAccess.Domain;

namespace SharpAccess.Diagnostics;

internal enum SharpAccessDiagnosticOperation
{
    Register,
    Login,
    Refresh,
    Logout,
    Revoke,
    CurrentUser,
    ChangePassword,
    ForgotPassword,
    ResetPassword,
    VerifyEmail,
    ResendVerification
}

internal static class SharpAccessDiagnostics
{
    internal const string ActivitySourceName = "SharpAccess";
    internal const string MeterName = "SharpAccess";
    internal const string InstrumentationVersion = "1.0.0";

    private const string OperationCancelledErrorType = "operation_cancelled";
    private const string UnexpectedExceptionErrorType = "unexpected_exception";

    private static readonly ActivitySource ActivitySource =
        new(ActivitySourceName, InstrumentationVersion);

    private static readonly Meter Meter =
        new(MeterName, InstrumentationVersion);

    private static readonly Counter<long> OperationCounter =
        Meter.CreateCounter<long>(
            "sharpaccess.auth.operations",
            description: "Counts bounded SharpAccess authentication operations.");

    private static readonly Counter<long> FailureCounter =
        Meter.CreateCounter<long>(
            "sharpaccess.auth.failures",
            description: "Counts failed or faulted SharpAccess authentication operations.");

    private static readonly Counter<long> AuditObservationFailureCounter =
        Meter.CreateCounter<long>(
            "sharpaccess.audit.observation_failures",
            description: "Counts failed best-effort persisted audit observations.");

    private static readonly Histogram<double> DurationHistogram =
        Meter.CreateHistogram<double>(
            "sharpaccess.auth.duration",
            unit: "ms",
            description: "Measures SharpAccess authentication operation duration.");

    // Records one failed observation write without adding caller-controlled dimensions.
    internal static void RecordAuditObservationFailure() =>
        AuditObservationFailureCounter.Add(1);

    // Tracks one bounded authentication operation without emitting caller or credential values.
    internal static async Task<ServiceResult<T>> TrackAsync<T>(
        SharpAccessDiagnosticOperation operation,
        Func<Task<ServiceResult<T>>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        string operationName = GetOperationName(operation);
        long started = Stopwatch.GetTimestamp();
        using Activity? activity = ActivitySource.StartActivity(
            $"sharpaccess.auth.{operationName}",
            ActivityKind.Internal);

        string outcome = "exception";
        string? errorType = null;

        try
        {
            ServiceResult<T> result = await action().ConfigureAwait(false);
            outcome = result.Succeeded ? "success" : "failure";
            errorType = result.Succeeded ? null : result.Error.ToString();
            activity?.SetStatus(
                result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                errorType);
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            errorType = OperationCancelledErrorType;
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
            throw;
        }
        catch (Exception)
        {
            errorType = UnexpectedExceptionErrorType;
            activity?.SetStatus(ActivityStatusCode.Error, errorType);
            throw;
        }
        finally
        {
            double elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            TagList tags = default;
            tags.Add("sharpaccess.operation", operationName);
            tags.Add("sharpaccess.outcome", outcome);
            if (errorType is not null)
            {
                tags.Add("sharpaccess.error.type", errorType);
            }

            activity?.SetTag("sharpaccess.operation", operationName);
            activity?.SetTag("sharpaccess.outcome", outcome);
            if (errorType is not null)
            {
                activity?.SetTag("sharpaccess.error.type", errorType);
            }

            OperationCounter.Add(1, tags);
            if (outcome is "failure" or "exception")
            {
                FailureCounter.Add(1, tags);
            }

            DurationHistogram.Record(elapsedMilliseconds, tags);
        }
    }

    // Maps an internal operation identifier to its stable low-cardinality telemetry value.
    private static string GetOperationName(SharpAccessDiagnosticOperation operation) =>
        operation switch
        {
            SharpAccessDiagnosticOperation.Register => "register",
            SharpAccessDiagnosticOperation.Login => "login",
            SharpAccessDiagnosticOperation.Refresh => "refresh",
            SharpAccessDiagnosticOperation.Logout => "logout",
            SharpAccessDiagnosticOperation.Revoke => "revoke",
            SharpAccessDiagnosticOperation.CurrentUser => "current_user",
            SharpAccessDiagnosticOperation.ChangePassword => "change_password",
            SharpAccessDiagnosticOperation.ForgotPassword => "forgot_password",
            SharpAccessDiagnosticOperation.ResetPassword => "reset_password",
            SharpAccessDiagnosticOperation.VerifyEmail => "verify_email",
            SharpAccessDiagnosticOperation.ResendVerification => "resend_verification",
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };
}
