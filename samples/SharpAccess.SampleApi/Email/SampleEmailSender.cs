using System.Text.Json;
using SharpAccess;

namespace SharpAccess.SampleApi;

internal sealed class SampleEmailSender(
    IWebHostEnvironment environment,
    ILogger<SampleEmailSender> logger) : IEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Action<ILogger, string, string, Exception?> LogEmailWritten = LoggerMessage.Define<string, string>(
        LogLevel.Information,
        new EventId(1, nameof(LogEmailWritten)),
        "Authentication email for {Recipient} written to {Path}.");

    // Implements send asynchronously for the sample support type.
    public async Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        if (!environment.IsDevelopment()
            && !string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The sample email sink is allowed only in Development or Test.");
        }

        string directory = Path.Combine(environment.ContentRootPath, "App_Data", "mail");
        Directory.CreateDirectory(directory);
        string filename = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
        string path = Path.Combine(directory, filename);
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4_096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, message, JsonOptions, cancellationToken).ConfigureAwait(false);
        LogEmailWritten(logger, message.Recipient, path, null);
    }
}
