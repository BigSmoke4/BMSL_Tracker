namespace BMSL_Tracker.Services;

public interface IAccountEmailSender
{
    bool IsConfigured { get; }

    Task SendConfirmationEmailAsync(string destination, string confirmationUrl, CancellationToken cancellationToken = default);
}
