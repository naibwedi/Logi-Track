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
        //try just for debug 
        try
        {
            //change the email 
            var from = new EmailAddress("123@hotmail.com", "123");
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