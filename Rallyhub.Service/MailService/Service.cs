using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Rallyhub.Service.MailService;

public class Service : IService
{
    private readonly MailOptions _mailOptions = new();
    private readonly ILogger<Service> _logger;

    public Service(IConfiguration configuration, ILogger<Service> logger)
    {
        configuration.GetSection(nameof(MailOptions)).Bind(_mailOptions);
        _logger = logger;
    }
    
    public async Task SendMail(MailContent mailContent)
    {
        try
        {
            _logger.LogInformation("Attempting to send email to {To} with subject {Subject}", mailContent.To, mailContent.Subject);

            MimeMessage email = new();
            email.Sender = new MailboxAddress(_mailOptions.DisplayName, _mailOptions.Mail);
            email.From.Add(new MailboxAddress(_mailOptions.DisplayName, _mailOptions.Mail));
            email.To.Add(MailboxAddress.Parse(mailContent.To));
            email.Subject = mailContent.Subject;

            BodyBuilder builder = new();
            builder.HtmlBody = mailContent.Body;
            email.Body = builder.ToMessageBody();

            // dùng SmtpClient của MailKit
            using SmtpClient smtp = new();

            await smtp.ConnectAsync(_mailOptions.Host, _mailOptions.Port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_mailOptions.Mail, _mailOptions.Password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {To}", mailContent.To);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}. Error: {Message}", mailContent.To, ex.Message);
            throw; // Re-throw to let caller handle
        }
    }
}