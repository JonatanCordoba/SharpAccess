namespace SharpAccess;

/// <summary>Sends authentication-related email messages supplied by SharpAccess.</summary>
public interface IEmailSender
{
    /// <summary>Sends an email message asynchronously.</summary>
    /// <param name="message">The complete message to deliver. Implementations must treat message bodies and recipients as sensitive application data.</param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>A task that represents the asynchronous delivery operation.</returns>
    Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Represents a complete authentication email without exposing persistence details.</summary>
/// <param name="Recipient">The destination email address.</param>
/// <param name="Subject">The message subject.</param>
/// <param name="TextBody">The required plain-text message body.</param>
/// <param name="HtmlBody">An optional HTML alternative body. Implementations remain responsible for safe rendering and transport.</param>
public sealed record AuthEmailMessage(
    string Recipient,
    string Subject,
    string TextBody,
    string? HtmlBody = null);

internal sealed class MissingEmailSender : IEmailSender
{
    /// <summary>Fails clearly when an enabled email flow has no sender implementation.</summary>
    public Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Email workflows are enabled, but no IEmailSender implementation was registered.");
    }
}
