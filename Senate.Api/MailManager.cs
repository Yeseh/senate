using SendGrid;
using SendGrid.Helpers.Mail;

namespace Senate.Api;

public static class MailManager
{
    private static SendGridClient? _client = null;

    private const string SENDER_EMAIL = "sendgrid@jessewellenberg.nl";

    public static SendGridClient GetClient(string apiKey)
    {
        if (_client != null) { return _client; }
        _client = new SendGridClient(apiKey);

        return _client;
    }
   
    public static async Task SendInvitation(SendGridClient client, string recipient, string invitationUrl)
    {
        var from = new EmailAddress(SENDER_EMAIL, "Senate");
        var to = new EmailAddress(recipient);
        var subject = "Senate Invitation";

        var content = $"Here's your invite url: {invitationUrl}";
        var html = $"<strong>{content}</strong>";
        var msg = MailHelper.CreateSingleEmail(from, to, subject, content, html);

        await client.SendEmailAsync(msg);
    } 
    
    public static async Task SendWelcome(SendGridClient client, string recipient)
    {
        var from = new EmailAddress(SENDER_EMAIL, "Senate");
        var to = new EmailAddress(recipient);
        var subject = "Welcome to Senate!";

        var content = $"Hope you have a good time";
        var html = $"<strong>{content}</strong>";
        var msg = MailHelper.CreateSingleEmail(from, to, subject, content, html);

        await client.SendEmailAsync(msg);
    }
}
