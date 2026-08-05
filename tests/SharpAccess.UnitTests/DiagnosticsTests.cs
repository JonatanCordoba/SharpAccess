using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharpAccess.Diagnostics;
using SharpAccess.Domain;
using SharpAccess.Services;

namespace SharpAccess.UnitTests;

[Collection(SharpAccessDiagnosticsTestGroup.Name)]
public sealed class DiagnosticsTests
{
    // Verifies that activity tags are bounded and exclude result codes or caller data.
    [Fact]
    public async Task AuthenticationActivityUsesSafeBoundedTags()
    {
        const string operation = "login";
        const string sensitiveCode = "raw-token-must-not-appear";
        ConcurrentQueue<Activity> stoppedActivities = new();

        using ActivityListener listener = CreateActivityListener(stoppedActivities);
        ActivitySource.AddActivityListener(listener);

        ServiceResult<bool> result = await SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Login,
            () => Task.FromResult(ServiceResult<bool>.Failure(AuthError.Unauthorized, sensitiveCode)));
        Assert.False(result.Succeeded);
        Activity activity = FindActivity(stoppedActivities, operation);
        Assert.Equal(operation, activity.GetTagItem("sharpaccess.operation"));
        Assert.Equal("failure", activity.GetTagItem("sharpaccess.outcome"));
        Assert.Equal(nameof(AuthError.Unauthorized), activity.GetTagItem("sharpaccess.error.type"));

        string serializedTags = SerializeActivityTags(activity);
        Assert.DoesNotContain(sensitiveCode, serializedTags, StringComparison.Ordinal);
    }

    // Verifies that the meter emits operation and duration measurements with low-cardinality tags.
    [Fact]
    public async Task AuthenticationMeterEmitsOperationAndDurationMeasurements()
    {
        const string operation = "refresh";
        ConcurrentQueue<MeasurementRecord> measurements = new();

        using MeterListener listener = CreateMeterListener(measurements);
        listener.Start();

        ServiceResult<bool> result = await SharpAccessDiagnostics.TrackAsync(
            SharpAccessDiagnosticOperation.Refresh,
            () => Task.FromResult(ServiceResult<bool>.Success(true)));
        Assert.True(result.Succeeded);
        Assert.Contains(
            measurements,
            item => item.Name == "sharpaccess.auth.operations"
                && item.Value == 1
                && item.Tags.TryGetValue("sharpaccess.operation", out string? value)
                && value == operation);
        Assert.Contains(
            measurements,
            item => item.Name == "sharpaccess.auth.duration"
                && item.Value >= 0
                && item.Tags.TryGetValue("sharpaccess.outcome", out string? value)
                && value == "success");
        Assert.DoesNotContain(
            measurements,
            item => item.Tags.Keys.Any(
                key => key.Contains("email", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("tenant", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("user", StringComparison.OrdinalIgnoreCase)));
    }

    // Verifies that host-defined exception names and messages never become telemetry dimensions.
    [Fact]
    public async Task UnexpectedExceptionsUseOneBoundedTelemetryCategory()
    {
        const string operation = "register";
        const string sensitiveMessage = "customer-specific-secret-detail";
        ConcurrentQueue<Activity> stoppedActivities = new();
        ConcurrentQueue<MeasurementRecord> measurements = new();

        using ActivityListener activityListener = CreateActivityListener(stoppedActivities);
        ActivitySource.AddActivityListener(activityListener);
        using MeterListener meterListener = CreateMeterListener(measurements);
        meterListener.Start();

        await Assert.ThrowsAsync<CustomerSpecificAuthenticationException>(
            () => SharpAccessDiagnostics.TrackAsync<bool>(
                SharpAccessDiagnosticOperation.Register,
                () => Task.FromException<ServiceResult<bool>>(
                    new CustomerSpecificAuthenticationException(sensitiveMessage))));

        Activity activity = FindActivity(stoppedActivities, operation);
        Assert.Equal("exception", activity.GetTagItem("sharpaccess.outcome"));
        Assert.Equal("unexpected_exception", activity.GetTagItem("sharpaccess.error.type"));

        string activityTags = SerializeActivityTags(activity);
        string measurementTags = SerializeMeasurementTags(measurements);
        Assert.DoesNotContain(nameof(CustomerSpecificAuthenticationException), activityTags, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(CustomerSpecificAuthenticationException), measurementTags, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, activityTags, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveMessage, measurementTags, StringComparison.Ordinal);
        Assert.Contains(
            measurements,
            item => item.Tags.TryGetValue("sharpaccess.error.type", out string? value)
                && value == "unexpected_exception");
    }

    // Verifies that cancellation uses a fixed category instead of framework exception metadata.
    [Fact]
    public async Task CancellationUsesOneBoundedTelemetryCategory()
    {
        const string operation = "refresh";
        ConcurrentQueue<Activity> stoppedActivities = new();
        using ActivityListener listener = CreateActivityListener(stoppedActivities);
        ActivitySource.AddActivityListener(listener);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SharpAccessDiagnostics.TrackAsync<bool>(
                SharpAccessDiagnosticOperation.Refresh,
                () => Task.FromCanceled<ServiceResult<bool>>(cancellation.Token)));

        Activity activity = FindActivity(stoppedActivities, operation);
        Assert.Equal("cancelled", activity.GetTagItem("sharpaccess.outcome"));
        Assert.Equal("operation_cancelled", activity.GetTagItem("sharpaccess.error.type"));
    }

    // Verifies that a failed standalone audit observation is measured without escaping to the request.
    [Fact]
    public async Task FailedAuditObservationIsMeasuredAndDoesNotEscape()
    {
        ConcurrentQueue<MeasurementRecord> measurements = new();
        IAuditService audit = new ThrowingAuditService(new InvalidOperationException("storage unavailable"));
        using MeterListener listener = CreateMeterListener(measurements);
        listener.Start();

        await audit.TryWriteObservationAsync(
            "login_failed",
            null,
            null,
            null,
            null,
            null);

        MeasurementRecord measurement = Assert.Single(
            measurements,
            item => item.Name == "sharpaccess.audit.observation_failures");
        Assert.Equal(1, measurement.Value);
        Assert.Empty(measurement.Tags);
    }

    // Verifies that caller-request cancellation remains observable and bypasses the audit store.
    [Fact]
    public async Task AuditObservationPreservesCallerCancellationBeforeWork()
    {
        ThrowingAuditService audit = new(new InvalidOperationException("must not run"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ((IAuditService)audit).TryWriteObservationAsync(
                "login_failed",
                null,
                null,
                null,
                null,
                null,
                cancellation.Token));

        Assert.Equal(0, audit.CallCount);
    }

    // Creates a listener that captures completed SharpAccess activities.
    private static ActivityListener CreateActivityListener(ConcurrentQueue<Activity> stoppedActivities) =>
        new()
        {
            ShouldListenTo = static source => source.Name == SharpAccessDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stoppedActivities.Enqueue(activity)
        };

    // Creates a listener that copies SharpAccess measurements before callback tag spans expire.
    private static MeterListener CreateMeterListener(ConcurrentQueue<MeasurementRecord> measurements)
    {
        MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == SharpAccessDiagnostics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
                measurements.Enqueue(new MeasurementRecord(
                    instrument.Name,
                    measurement,
                    CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
                measurements.Enqueue(new MeasurementRecord(
                    instrument.Name,
                    measurement,
                    CopyTags(tags))));
        return listener;
    }

    // Finds the completed activity for one bounded authentication operation.
    private static Activity FindActivity(ConcurrentQueue<Activity> activities, string operation) =>
        activities.First(item => item.OperationName == $"sharpaccess.auth.{operation}");

    // Serializes activity tags only for redaction assertions.
    private static string SerializeActivityTags(Activity activity) =>
        string.Join("|", activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));

    // Serializes copied measurement tags only for redaction assertions.
    private static string SerializeMeasurementTags(ConcurrentQueue<MeasurementRecord> measurements) =>
        string.Join(
            "|",
            measurements.SelectMany(item => item.Tags.Select(tag => $"{tag.Key}={tag.Value}")));

    // Copies measurement tags before the callback span becomes invalid.
    private static Dictionary<string, string?> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, string?> result = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            result[tag.Key] = tag.Value?.ToString();
        }

        return result;
    }

    private sealed record MeasurementRecord(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string?> Tags);

    private sealed class CustomerSpecificAuthenticationException : Exception
    {
        // Creates a host-defined exception with content that must not enter telemetry.
        internal CustomerSpecificAuthenticationException(string message)
            : base(message)
        {
        }
    }

    private sealed class ThrowingAuditService(Exception exception) : IAuditService
    {
        internal int CallCount { get; private set; }

        // Simulates one failed persistence call for observation-policy verification.
        public Task WriteAsync(
            string eventType,
            Guid? userId,
            Guid? tenantId,
            string? ipAddress,
            string? userAgent,
            string? detail,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromException(exception);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharpAccessDiagnosticsTestGroup
{
    public const string Name = "SharpAccess diagnostics";
}
