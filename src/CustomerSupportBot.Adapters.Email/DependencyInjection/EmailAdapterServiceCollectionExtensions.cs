using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Email.DependencyInjection;

public static class EmailAdapterServiceCollectionExtensions
{
    /// <summary>
    /// <c>Email:Enabled</c>'a göre SMTP ya da gönderimsiz adaptör. Ayarların bağlanması ve doğrulaması
    /// Application katmanındadır (<see cref="EmailOptions"/>).
    /// </summary>
    public static IServiceCollection AddEmailAdapter(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmailOptions>>();
            return options.Value.Enabled
                ? new SmtpEmailSender(options, sp.GetRequiredService<ILogger<SmtpEmailSender>>())
                : new NullEmailSender(sp.GetRequiredService<ILogger<NullEmailSender>>());
        });
        return services;
    }
}
