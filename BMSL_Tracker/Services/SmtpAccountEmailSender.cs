using System.Net;
using System.Net.Mail;

namespace BMSL_Tracker.Services;

public sealed class SmtpAccountEmailSender : IAccountEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpAccountEmailSender> _logger;

    public SmtpAccountEmailSender(IConfiguration configuration, ILogger<SmtpAccountEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Host"])
        && !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:FromAddress"]);

    public async Task SendConfirmationEmailAsync(
        string destination,
        string confirmationUrl,
        CancellationToken cancellationToken = default)
    {
        var host = _configuration["Email:Smtp:Host"];
        var fromAddress = _configuration["Email:Smtp:FromAddress"];
        if (!IsConfigured || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        }

        var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;
        var userName = _configuration["Email:Smtp:UserName"];
        var password = _configuration["Email:Smtp:Password"];
        var enableSsl = _configuration.GetValue("Email:Smtp:EnableSsl", true);

        using var message = new MailMessage(fromAddress, destination)
        {
            Subject = "Confirm your BMSL Tracker email",
            Body = $"Confirm your email address for BMSL Tracker by opening this link:\n\n{confirmationUrl}\n\nIf you did not request this account, you can ignore this message.",
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(userName))
        {
            client.Credentials = new NetworkCredential(userName, password ?? string.Empty);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Could not send an account confirmation email.");
            throw;
        }
    }
}
