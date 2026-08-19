// Tests/A2APartnerSeedTests.cs
//
// A2A partner hesabı YALNIZCA açıkça yapılandırıldığında oluşturulmalıdır.
//
// Eskiden tek koşul A2A:Enabled idi ve kullanıcı adı/parola verilmezse "demo-partner" /
// "Partner123!" varsayılanları kullanılıyordu. Üretimde kanal açılıp bu anahtarlar unutulursa,
// parolası bu depoda açıkça yazılı olan (samples/.../appsettings.json) bir Partner hesabı
// sessizce oluşuyordu.
//
// Ortam kontrolü (yalnızca Development'ta seed et) bu iş için yeterli DEĞİLDİR:
// ASPNETCORE_ENVIRONMENT yanlış ayarlanmış bir kurulumda hesap yine oluşur ve hata sessizdir.
// Açık yapılandırma zorunluluğu yanlış ayarlanamaz — anahtar yoksa hesap da yoktur.

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Api.IntegrationTests;

public class A2APartnerSeedTests
{
    /// <summary>A2A açık, partner kimlik bilgileri VERİLMEMİŞ.</summary>
    private sealed class NoPartnerCredentialsFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("A2A:Enabled", "true");
            builder.UseSetting("A2A:DevPartnerUsername", "");
            builder.UseSetting("A2A:DevPartnerPassword", "");
        }
    }

    /// <summary>A2A açık, kimlik bilgileri AÇIKÇA verilmiş.</summary>
    private sealed class WithPartnerCredentialsFactory : TestWebApplicationFactory
    {
        public const string Username = "explicit-partner";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("A2A:Enabled", "true");
            builder.UseSetting("A2A:DevPartnerUsername", Username);
            builder.UseSetting("A2A:DevPartnerPassword", "Explicit#Pass1");
        }
    }

    [Fact]
    public async Task PartnerAccount_IsNotSeeded_WhenCredentialsAreNotConfigured()
    {
        await using var factory = new NoPartnerCredentialsFactory();
        _ = factory.CreateClient();   // host'u başlat → seeder çalışsın

        await using var ctx = await factory.GetDbContextFactory()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        var partners = await ctx.Users
            .Where(u => u.Role == "Partner")
            .Select(u => u.Username)
            .ToListAsync(TestContext.Current.CancellationToken);

        partners.Should().BeEmpty(
            "kimlik bilgileri verilmediğinde hiçbir partner hesabı oluşturulmamalı");
        partners.Should().NotContain("demo-partner",
            "parolası bu depoda açıkça yazılı olan varsayılan hesap asla kendiliğinden oluşmamalı");
    }

    [Fact]
    public async Task PartnerAccount_IsSeeded_WhenCredentialsAreExplicit()
    {
        // Karşı taraf: zorunluluk, geliştirme akışını da bozmamalı.
        await using var factory = new WithPartnerCredentialsFactory();
        _ = factory.CreateClient();

        await using var ctx = await factory.GetDbContextFactory()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);

        var exists = await ctx.Users.AnyAsync(
            u => u.Username == WithPartnerCredentialsFactory.Username && u.Role == "Partner",
            TestContext.Current.CancellationToken);

        exists.Should().BeTrue("açıkça istenen partner hesabı oluşturulmalı");
    }
}
