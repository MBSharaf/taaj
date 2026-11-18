using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using LabStage.Application.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LabStage.Infrastructure.Email;

/// <summary>
/// Picks the email provider from configuration (secrets via user-secrets / environment variables, never committed):
///   Email:SendGrid:ApiKey            -> SendGrid
///   Email:Smtp:Host (+Port/User/Password/EnableSsl) -> SMTP
///   none + Development               -> files in App_Data/outbox (open them in a browser)
///   none + Production                -> nothing is sent; a warning is logged
/// Email:From / Email:FromName set the sender.
/// </summary>
public static class EmailSenderFactory
{
    public static IEmailSender Create(IConfiguration config, string contentRoot, bool isDevelopment, ILoggerFactory logs)
    {
        var from = config["Email:From"] ?? "no-reply@taaj.local";
        var fromName = config["Email:FromName"] ?? "تاج Taaj";
        if (!string.IsNullOrWhiteSpace(config["Email:SendGrid:ApiKey"]))
            return new SendGridEmailSender(config["Email:SendGrid:ApiKey"]!, from, fromName);
        if (!string.IsNullOrWhiteSpace(config["Email:Smtp:Host"]))
            return new SmtpEmailSender(config, from, fromName);
        if (isDevelopment)
            return new FileEmailSender(Path.Combine(contentRoot, "App_Data", "outbox"), logs.CreateLogger<FileEmailSender>());
        return new NoEmailSender(logs.CreateLogger<NoEmailSender>());
    }
}

/// <summary>Development: every email is written as an .html file (newest first by name) instead of being sent.</summary>
public sealed class FileEmailSender(string folder, ILogger<FileEmailSender> log) : IEmailSender
{
    public string Provider => "File";
    public string Folder => folder;

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var safeTo = string.Concat(m.To.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safeTo}.html");
        var html = $"<!doctype html><meta charset=\"utf-8\"><p><b>To:</b> {WebUtility.HtmlEncode(m.To)}<br><b>Subject:</b> {WebUtility.HtmlEncode(m.Subject)}</p><hr>{m.Html}";
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, ct);
        log.LogInformation("Email to {To} written to {Path} (development outbox)", m.To, path);
    }
}

/// <summary>Production without a provider: emails are not sent (configure Email:SendGrid or Email:Smtp).</summary>
public sealed class NoEmailSender(ILogger<NoEmailSender> log) : IEmailSender
{
    public string Provider => "None";

    public Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        log.LogWarning("No email provider configured: \"{Subject}\" to {To} was not sent.", m.Subject, m.To);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailSender(IConfiguration config, string from, string fromName) : IEmailSender
{
    public string Provider => "Smtp";

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        using var client = new SmtpClient(config["Email:Smtp:Host"], int.TryParse(config["Email:Smtp:Port"], out var port) ? port : 587)
        {
            EnableSsl = !string.Equals(config["Email:Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase),
        };
        if (!string.IsNullOrEmpty(config["Email:Smtp:User"]))
            client.Credentials = new NetworkCredential(config["Email:Smtp:User"], config["Email:Smtp:Password"]);
        using var msg = new MailMessage { From = new MailAddress(from, fromName), Subject = m.Subject, Body = m.Html, IsBodyHtml = true, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        msg.To.Add(m.To);
        msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(m.Text, Encoding.UTF8, "text/plain"));
        await client.SendMailAsync(msg, ct);
    }
}

public sealed class SendGridEmailSender(string apiKey, string from, string fromName) : IEmailSender
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.sendgrid.com/") };
    public string Provider => "SendGrid";

    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v3/mail/send")
        {
            Content = JsonContent.Create(new
            {
                personalizations = new[] { new { to = new[] { new { email = m.To } } } },
                from = new { email = from, name = fromName },
                subject = m.Subject,
                content = new[] { new { type = "text/plain", value = m.Text }, new { type = "text/html", value = m.Html } },
            }),
        };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
    }
}
