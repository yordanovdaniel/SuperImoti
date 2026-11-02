using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SuperImoti.Web.Data;
using SuperImoti.Web.Infrastructure;
using SuperImoti.Web.Models;

namespace SuperImoti.Web.Services;

/// <summary>E-mail settings, bound from the "Email" configuration section.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string FromAddress { get; set; } = "noreply@superimoti.local";

    public string FromName { get; set; } = "SuperImoti";

    /// <summary>Leave empty to write e-mails as .eml files into <see cref="PickupDirectory"/> instead of sending them.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string PickupDirectory { get; set; } = "App_Data/mail";
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody);

/// <summary>In-memory queue, so a page never waits for the mail server.</summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateUnbounded<EmailMessage>();

    public bool Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Sends through SMTP when configured; otherwise drops .eml files into the pickup folder.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IWebHostEnvironment env)
{
    public string PickupDirectory
    {
        get
        {
            var dir = options.Value.PickupDirectory;
            return Path.IsPathRooted(dir) ? dir : Path.Combine(env.ContentRootPath, dir);
        }
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var settings = options.Value;

        using var mail = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        mail.To.Add(message.To);

        using var client = new SmtpClient();
        if (string.IsNullOrWhiteSpace(settings.SmtpHost))
        {
            Directory.CreateDirectory(PickupDirectory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = PickupDirectory;
        }
        else
        {
            client.Host = settings.SmtpHost;
            client.Port = settings.SmtpPort;
            client.EnableSsl = settings.EnableSsl;
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
            }
        }

        await client.SendMailAsync(mail, ct);
    }
}

/// <summary>Takes e-mails from the queue and sends them one by one in the background.</summary>
public sealed class EmailBackgroundService(
    EmailQueue queue,
    SmtpEmailSender sender,
    ILogger<EmailBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await sender.SendAsync(message, stoppingToken);
                    logger.LogInformation("E-mail \"{Subject}\" sent to {To}.", message.Subject, message.To);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Could not send e-mail to {To}.", message.To);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The site is shutting down.
        }
    }
}

/// <summary>E-mails every subscribed user when someone publishes a new listing.</summary>
public class NewListingNotifier(ApplicationDbContext db, EmailQueue queue)
{
    public async Task<int> NotifyAsync(Listing listing, string districtName, string listingUrl, CancellationToken ct)
    {
        var recipients = await db.Users.AsNoTracking()
            .Where(u => u.NotifyAboutNewListings && u.Id != listing.OwnerId && u.Email != null)
            .Select(u => new { u.Email, u.DisplayName })
            .ToListAsync(ct);

        var title = WebUtility.HtmlEncode(listing.Title);
        var details = WebUtility.HtmlEncode(
            $"{listing.DealKind.GetDisplayName()} · {districtName} · {Fmt.Area(listing.Area)} · {Fmt.Money(listing.Price, listing.Currency)}");
        var url = WebUtility.HtmlEncode(listingUrl);

        foreach (var recipient in recipients)
        {
            var body = $"""
                <p>Здравейте, {WebUtility.HtmlEncode(recipient.DisplayName)}!</p>
                <p>В SuperImoti току-що беше публикувана нова обява:</p>
                <p><strong>{title}</strong><br>{details}</p>
                <p><a href="{url}">Вижте обявата</a></p>
                <p style="color:#6c757d;font-size:12px">Получавате този имейл, защото сте включили известията в профила си.</p>
                """;

            queue.Enqueue(new EmailMessage(recipient.Email!, $"Нова обява: {listing.Title}", body));
        }

        return recipients.Count;
    }
}
