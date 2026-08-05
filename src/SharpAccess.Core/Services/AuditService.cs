using SharpAccess.Abstractions;
using SharpAccess.Diagnostics;
using SharpAccess.Domain;
using SharpAccess.Persistence;

namespace SharpAccess.Services;

internal interface IAuditService
{
    // Persists a sanitized security event without raw credentials or tokens.
    Task WriteAsync(
        string eventType,
        Guid? userId,
        Guid? tenantId,
        string? ipAddress,
        string? userAgent,
        string? detail,
        CancellationToken cancellationToken = default);

    // Attempts one standalone observation without changing an already-determined request result.
    async Task TryWriteObservationAsync(
        string eventType,
        Guid? userId,
        Guid? tenantId,
        string? ipAddress,
        string? userAgent,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await WriteAsync(
                eventType,
                userId,
                tenantId,
                ipAddress,
                userAgent,
                detail,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SharpAccessDiagnostics.RecordAuditObservationFailure();
        }
    }
}

internal sealed class AuditService(IAuthAuditStore store, IAuthClock clock) : IAuditService
{
    // Persists a sanitized security event without raw credentials or tokens.
    public Task WriteAsync(
        string eventType,
        Guid? userId,
        Guid? tenantId,
        string? ipAddress,
        string? userAgent,
        string? detail,
        CancellationToken cancellationToken = default)
    {
        return store.WriteAuditAsync(
            SecurityAuditEvidence.Create(
                clock.UtcNow,
                eventType,
                userId,
                tenantId,
                ipAddress,
                userAgent,
                detail),
            cancellationToken);
    }
}
