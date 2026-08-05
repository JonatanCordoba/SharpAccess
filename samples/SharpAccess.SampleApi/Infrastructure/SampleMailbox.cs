using System.Collections.Concurrent;

namespace SharpAccess.SampleApi;

internal sealed class SampleMailbox : IEmailSender
{
    private readonly ConcurrentDictionary<string, AuthEmailMessage> _messages =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        _messages[message.Recipient] = message;
        Console.WriteLine($"[Sample email] {message.Subject} -> {message.Recipient}");
        return Task.CompletedTask;
    }

    internal async Task<AuthEmailMessage> WaitForAsync(
        string recipient,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        while (!linked.IsCancellationRequested)
        {
            if (_messages.TryGetValue(recipient, out AuthEmailMessage? message))
            {
                return message;
            }

            await Task.Delay(25, linked.Token).ConfigureAwait(false);
        }

        throw new TimeoutException($"No sample email was captured for '{recipient}'.");
    }
}
