// E-posta ayarları: açıkken eksik ayar başlangıçta hata vermeli (sessizce gönderilmeyen e-posta olmasın).

using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Tests;

public class EmailOptionsValidatorTests
{
    private static readonly EmailOptionsValidator Validator = new();

    [Fact]
    public void Disabled_IsAlwaysValid() =>
        Validator.Validate(null, new EmailOptions { Enabled = false }).Succeeded.Should().BeTrue();

    [Fact]
    public void Enabled_WithoutHostOrFrom_Fails()
    {
        var result = Validator.Validate(null, new EmailOptions { Enabled = true });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Email:Smtp:Host").And.Contain("Email:FromAddress");
    }

    [Fact]
    public void Enabled_WithValidSettings_Succeeds() =>
        Validator.Validate(null, new EmailOptions
        {
            Enabled = true, FromAddress = "destek@example.com", PublicBaseUrl = "https://destek.example.com",
            Smtp = new EmailOptions.SmtpOptions { Host = "smtp.example.com", Port = 465, Security = SmtpSecurity.SslOnConnect }
        }).Succeeded.Should().BeTrue();

    [Fact]
    public void Enabled_WithRelativeBaseUrl_Fails() =>
        Validator.Validate(null, new EmailOptions
        {
            Enabled = true, FromAddress = "destek@example.com", PublicBaseUrl = "/sohbet",
            Smtp = new EmailOptions.SmtpOptions { Host = "smtp.example.com" }
        }).FailureMessage.Should().Contain("PublicBaseUrl");
}
