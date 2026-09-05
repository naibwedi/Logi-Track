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
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly bool _isConfigured;

    public EmailSender(ISendGridClient sendGridClient, ILogger<EmailSender> logger, IConfiguration configuration)
    {
        _sendGridClient = sendGridClient;
        _logger = logger;
        _fromEmail = configuration["SendGrid:FromEmail"] ?? string.Empty;
        _fromName = configuration["SendGrid:FromName"] ?? "Logi-Track";
        _isConfigured = !string.IsNullOrWhiteSpace(configuration["SendGrid:ApiKey"])
            && !string.IsNullOrWhiteSpace(_fromEmail);
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    
    {
        //try just for debug 
        try
        {
            if (!_isConfigured)
            {
                _logger.LogWarning("Email delivery skipped because SendGrid is not configured.");
                return;
            }

            var from = new EmailAddress(_fromEmail, _fromName);
            var to = new EmailAddress(email);
            //include the html content 
            var plainTextContent = htmlMessage.Replace("<br>", "\n").Replace("<br/>", "\n");
            //i removed the null
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent: htmlMessage);
            var response = await _sendGridClient.SendEmailAsync(msg);
            //debug 
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent successfully");
            }
            else
            {
                _logger.LogError("Email sending failed");

                throw new Exception(response.ToString()); 
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email sending failed");
            throw;
        }
        
    }
}
