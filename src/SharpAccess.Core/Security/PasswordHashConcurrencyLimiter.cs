using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SharpAccess.Configuration;

namespace SharpAccess.Security;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Shared process-wide semaphore instances live for the process lifetime.")]
internal sealed class PasswordHashConcurrencyLimiter
{
    private static readonly ConcurrentDictionary<string, PasswordHashConcurrencyLimiter> Shared = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maximumQueued;
    private readonly TimeSpan _queueTimeout;
    private int _queued;

    // Creates one process-wide capacity and queue boundary for a unique option set.
    private PasswordHashConcurrencyLimiter(PasswordSecurityOptions options)
    {
        _semaphore = new SemaphoreSlim(options.MaximumConcurrentPasswordHashes, options.MaximumConcurrentPasswordHashes);
        _maximumQueued = options.MaximumQueuedPasswordHashes;
        _queueTimeout = options.PasswordHashQueueTimeout;
    }

    // Gets the shared limiter matching the configured concurrency, queue, and timeout values.
    internal static PasswordHashConcurrencyLimiter Get(PasswordSecurityOptions options)
    {
        string key = FormattableString.Invariant(
            $"{options.MaximumConcurrentPasswordHashes}:{options.MaximumQueuedPasswordHashes}:{options.PasswordHashQueueTimeout.Ticks}");
        return Shared.GetOrAdd(key, _ => new PasswordHashConcurrencyLimiter(options));
    }

    internal int QueuedCount => Volatile.Read(ref _queued);

    // Acquires immediate capacity or waits inside the configured bounded queue.
    internal async ValueTask<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_semaphore.Wait(0, cancellationToken))
        {
            SharpAccessSecurityMetrics.ActivePasswordHashes.Add(1);
            return new Lease(_semaphore);
        }

        if (_maximumQueued == 0)
        {
            throw new InvalidOperationException("The bounded password-hash queue is full.");
        }

        int queued = Interlocked.Increment(ref _queued);
        if (queued > _maximumQueued)
        {
            Interlocked.Decrement(ref _queued);
            throw new InvalidOperationException("The bounded password-hash queue is full.");
        }

        long started = Stopwatch.GetTimestamp();
        try
        {
            using CancellationTokenSource timeout = new(_queueTimeout);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out while waiting for bounded password-hash capacity.");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            SharpAccessSecurityMetrics.PasswordHashQueueDuration.Record(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        SharpAccessSecurityMetrics.ActivePasswordHashes.Add(1);
        return new Lease(_semaphore);
    }

    internal sealed class Lease : IAsyncDisposable, IDisposable
    {
        private SemaphoreSlim? _owner;

        // Creates a single-release lease for one acquired hash slot.
        internal Lease(SemaphoreSlim owner) => _owner = owner;

        // Releases the acquired hash slot at most once.
        public void Dispose()
        {
            SemaphoreSlim? owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            SharpAccessSecurityMetrics.ActivePasswordHashes.Add(-1);
            owner.Release();
        }

        // Releases the acquired hash slot through the asynchronous disposal contract.
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
