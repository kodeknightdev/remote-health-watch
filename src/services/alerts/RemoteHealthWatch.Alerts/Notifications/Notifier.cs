using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;
using RemoteHealthWatch.Alerts.Domain;
using RemoteHealthWatch.Alerts.Services;
using RemoteHealthWatch.Contracts;

namespace RemoteHealthWatch.Alerts.Notifications;

public sealed class NotificationOptions
{
    public string SmtpHost { get; set; } = "localhost";
    public int SmtpPort { get; set; } = 1025;
    public string From { get; set; } = "alerts@remotehealthwatch.local";
    public string DashboardUrl { get; set; } = "http://localhost:4200";
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public interface ISmsSender
{
    Task SendAsync(string phone, string text, CancellationToken ct);
}

public sealed class SmtpEmailSender(IOptions<NotificationOptions> options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(o.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.SmtpHost, o.SmtpPort, MailKit.Security.SecureSocketOptions.None, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}

/// <summary>No SMS gateway in the demo; the message is logged with the number masked.</summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phone, string text, CancellationToken ct)
    {
        var masked = phone.Length > 4 ? new string('*', phone.Length - 4) + phone[^4..] : "****";
        logger.LogInformation("SMS to {MaskedPhone}: {Text}", masked, text);
        return Task.CompletedTask;
    }
}

public interface IAlertNotifier
{
    Task NotifyAsync(Alert alert, string role, bool final, CancellationToken ct);
}

/// <summary>Works out who to page for a role and sends email plus SMS. Content carries no patient identifiers beyond the surrogate id.</summary>
public sealed class AlertNotifier(
    CareTeamDirectory careTeams,
    StaffDirectory staff,
    IEmailSender email,
    ISmsSender sms,
    IOptions<NotificationOptions> options,
    ILogger<AlertNotifier> logger) : IAlertNotifier
{
    public async Task NotifyAsync(Alert alert, string role, bool final, CancellationToken ct)
    {
        try
        {
            var recipients = await RecipientsAsync(alert.PatientId, role, ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning("No {Role} to notify for alert {AlertId}", role, alert.Id);
                return;
            }
            var link = $"{options.Value.DashboardUrl.TrimEnd('/')}/alerts/{alert.Id}";
            var prefix = final ? "UNANSWERED " : alert.EscalationLevel > 0 ? "ESCALATED " : "";
            var subject = $"[{alert.Severity}] {prefix}{alert.Type} alert for patient {alert.PatientId.ToString()[..8]}";
            var body = $"{alert.Message}\nRaised {alert.RaisedAt:u}. Assigned to {role}.\nOpen: {link}";
            foreach (var r in recipients)
            {
                await email.SendAsync(r.Email, subject, body, ct);
                if (!string.IsNullOrWhiteSpace(r.Phone) && alert.Severity >= Contracts.Events.AlertSeverity.High)
                    await sms.SendAsync(r.Phone, $"{subject} {link}", ct);
            }
            AlertMetrics.Notifications.Add(recipients.Count, new KeyValuePair<string, object?>("role", role));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // paging is best effort; the alert itself is already stored and visible on the dashboard
            logger.LogWarning("Notification for alert {AlertId} to {Role} failed: {Error}", alert.Id, role, ex.Message);
        }
    }

    internal async Task<IReadOnlyList<StaffContact>> RecipientsAsync(Guid patientId, string role, CancellationToken ct)
    {
        var team = await careTeams.CareTeamAsync(patientId, ct);
        var matching = team.Where(m => m.Role == role).OrderByDescending(m => m.IsPrimary).ToList();
        if (role == Roles.Nurse && matching.Count > 0) return matching.Take(1).ToList();
        if (matching.Count > 0) return matching;

        // nobody on the care team for that role: fall back to whoever is on call
        var all = await staff.AllAsync(ct);
        var fallback = role == Roles.Doctor ? all.Where(s => s.Role == Roles.Doctor && s.IsOnCall) : all.Where(s => s.Role == role);
        return fallback.Select(s => new StaffContact(s.Id, s.Role, s.DisplayName, s.Email, "", false)).ToList();
    }
}
