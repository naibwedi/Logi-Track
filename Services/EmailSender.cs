using Microsoft.AspNetCore.Identity.UI.Services;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;
//evan code i just move it to different folder 
namespace logirack.Services;
public class EmailSender : IEmailSender
{
    private readonly ISendGridClient _sendGridClient;
    private readonly ILogger _logger;

    public EmailSender(ISendGridClient sendGridClient , ILogger<EmailSender> logger)
    {
        _sendGridClient = sendGridClient;
        _logger = logger;

    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var from = new EmailAddress("logirack694@gmail.com", "Logirack");
        var to = new EmailAddress(email);
        var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent: null, htmlContent: htmlMessage);

        var response = await _sendGridClient.SendEmailAsync(msg);

        // Optionally, handle the response
        if (response.StatusCode >= System.Net.HttpStatusCode.BadRequest)
        {
            // Log or handle the error as needed
            //throw new Exception($"Failed to send email: {response.StatusCode}");
            _logger.LogError( "Error sending email to {Email}", email);

        }
    }
}