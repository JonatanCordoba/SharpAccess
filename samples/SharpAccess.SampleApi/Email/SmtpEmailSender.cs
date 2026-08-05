using System.Net;
using System.Net.Mail;
using SharpAccess;

namespace SharpAccess.SampleApi;

internal sealed class SmtpEmailSender(
    string host,
    int port,
    string username,
    string password,
    string fromAddress) : IEmailSender
{
    // Sends one authentication message through the production SMTP relay.
    public async Task SendAsync(AuthEmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        using MailMessage mail = new()
        {
            From = new MailAddress(fromAddress),
            Subject = message.Subject,
            Body = message.HtmlBody ?? message.TextBody,
            IsBodyHtml = message.HtmlBody is not null
        };
        mail.To.Add(new MailAddress(message.Recipient));
        using SmtpClient client = new(host, port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password)
        };
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}
