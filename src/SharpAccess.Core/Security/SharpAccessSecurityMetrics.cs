using System.Diagnostics.Metrics;

namespace SharpAccess.Security;

internal static class SharpAccessSecurityMetrics
{
    private static readonly Meter Meter = new("SharpAccess.Security", "1.0.0");

    internal static readonly Histogram<double> PasswordHashQueueDuration =
        Meter.CreateHistogram<double>("sharpaccess.password_hash.queue.duration", "ms");

    internal static readonly Histogram<double> PasswordHashDuration =
        Meter.CreateHistogram<double>("sharpaccess.password_hash.duration", "ms");

    internal static readonly UpDownCounter<long> ActivePasswordHashes =
        Meter.CreateUpDownCounter<long>("sharpaccess.password_hash.active");

    internal static readonly Counter<long> PasswordHashesRequiringRehash =
        Meter.CreateCounter<long>("sharpaccess.password_hash.rehash_required");

    internal static readonly Histogram<long> EncodedAccessTokenSize =
        Meter.CreateHistogram<long>("sharpaccess.access_token.encoded_size", "By");
}
