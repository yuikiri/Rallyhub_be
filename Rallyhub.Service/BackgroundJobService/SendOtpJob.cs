using Microsoft.Extensions.Logging;
using Quartz;
using Rallyhub.Service.MailService;

namespace Rallyhub.Service.BackgroundJobService;

public class SendOtpJob : IJob
{
    private readonly IService _mailService;
    private readonly ILogger<SendOtpJob> _logger;

    public SendOtpJob(IService mailService, ILogger<SendOtpJob> logger)
    {
        _mailService = mailService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        string email = context.MergedJobDataMap.GetString("Email")!;
        string otpCode = context.MergedJobDataMap.GetString("OtpCode")!;
        string actionType = context.MergedJobDataMap.GetString("ActionType")!;

        _logger.LogInformation("Starting SendOtpJob for {Email}, ActionType: {ActionType}", email, actionType);

        string subject = "";
        string htmlBody = "";
        
        switch (actionType)
        {
            case "Register":
                subject = "Người tình trong mộng Ralluhub";
                htmlBody = MailTemplate.GenerateOtpRegisterTemplate(email, otpCode);
                break;
            
            case "ForgotPassword":
                subject = "Người tình trong mộng Ralluhub";
                htmlBody = MailTemplate.GeneratePasswordResetTemplate(email, otpCode);
                break;
            // case "Approval":
            //     subject = "Người tình trong mộng Ralluhub";
            //     htmlBody = MailTemplate.GenerateApprovalTemplate(email);
            //     break;
            // case "Rejection":
            //     subject = "rallyhub - yêu cầu khôi phục mật khẩu";
            //     htmlBody = MailTemplate.GenerateRejectionTemplate(email, );
            //     break;
        }

        try
        {
            await _mailService.SendMail(new MailContent()
            {
                To = email,
                Subject = subject,
                Body = htmlBody
            });
            _logger.LogInformation("SendOtpJob completed successfully for {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendOtpJob failed for {Email}. Error: {Message}", email, ex.Message);
            // Optionally, re-throw or handle
        }
    }
}