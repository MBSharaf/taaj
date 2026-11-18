namespace LabStage.Application.Email;

/// <summary>A message to one recipient. Html is the main body; Text is the plain-text fallback.</summary>
public sealed record EmailMessage(string To, string Subject, string Html, string Text);

/// <summary>Sends account emails (password reset, email confirmation, staff invitations).</summary>
public interface IEmailSender
{
    /// <summary>Short name of the active provider (for logs and health info): File, Smtp, SendGrid or None.</summary>
    string Provider { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
