// Çıktı guardrail'i: yanıt müşteriye ulaşmadan ÖNCE deterministik kontrol.
//   - Konuşmada geçmeyen/müşteriye ait olmayan e-posta ve telefon → maskelenir (Redacted)
//   - Gizli anahtar / JWT / bağlantı dizesi / sistem istemi sızıntısı → yanıt tamamen değiştirilir (Blocked)

using CustomerSupportBot.Application.Services.Chat;

namespace CustomerSupportBot.Application.Tests;

public class OutputGuardrailTests
{
    private static OutputGuardContext Ctx(
        string[]? userText = null, string? ownEmail = null, string[]? allowed = null, string[]? phrases = null) =>
        new(userText ?? [], ownEmail, allowed ?? [], phrases ?? []);

    [Fact]
    public void ACleanAnswer_PassesUnchanged()
    {
        var r = OutputGuardrail.Inspect("Siparişiniz #1044 kargoda, takip no YK123456789012.", Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
        r.Text.Should().Be("Siparişiniz #1044 kargoda, takip no YK123456789012.");
    }

    [Fact]
    public void AForeignEmail_IsMasked()
    {
        var r = OutputGuardrail.Inspect("Başka müşteri: ali.veli@example.com yazmış.", Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Redacted);
        r.Text.Should().NotContain("ali.veli@example.com").And.Contain(OutputGuardrail.RedactedMark);
        r.Findings.Should().Contain("email");
    }

    [Fact]
    public void TheCustomersOwnEmail_IsAllowed()
    {
        var r = OutputGuardrail.Inspect("Bildirim ayse@example.com adresine gitti.", Ctx(ownEmail: "Ayse@Example.com"));

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Fact]
    public void AnEmailTheCustomerTyped_IsAllowed()
    {
        var r = OutputGuardrail.Inspect("ben@firma.com adresine göndereceğiz.", Ctx(userText: ["mailim ben@firma.com"]));

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Fact]
    public void AnAllowlistedSupportContact_IsAllowed()
    {
        var r = OutputGuardrail.Inspect("Bize destek@sirket.com veya 0850 123 45 67 üzerinden ulaşın.",
            Ctx(allowed: ["destek@sirket.com", "0850 123 45 67"]));

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Theory]
    [InlineData("Aradığı numara 0532 123 45 67 idi.")]
    [InlineData("Numara +90 532 123 45 67.")]
    [InlineData("Numara 05321234567.")]
    [InlineData("Numara (0532) 123-45-67.")]
    public void AForeignPhone_IsMasked_InCommonFormats(string answer)
    {
        var r = OutputGuardrail.Inspect(answer, Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Redacted);
        r.Text.Should().NotMatchRegex(@"532.?123").And.Contain(OutputGuardrail.RedactedMark);
        r.Findings.Should().Contain("phone");
    }

    [Fact]
    public void APhoneInADifferentFormat_ThatTheCustomerTyped_IsAllowed()
    {
        var r = OutputGuardrail.Inspect("0532 123 45 67 numaranızı kaydettik.", Ctx(userText: ["numaram +90 (532) 123 45 67"]));

        r.Verdict.Should().Be(OutputGuardVerdict.Clean, "karşılaştırma yalnızca rakamlarla yapılır");
    }

    [Theory]
    [InlineData("Sipariş 1044, tutar 1.250,00 TL")]
    [InlineData("Takip: YK123456789012")]
    [InlineData("Takip no 4012345678 ile sorgulayın")]   // ayraçsız ve 0/+90 önekisiz → telefon sayılmaz
    [InlineData("Teslimat 21.10.2026 tarihinde")]
    public void OrderNumbersAmountsAndTrackingNumbers_AreNotPhones(string answer) =>
        OutputGuardrail.Inspect(answer, Ctx()).Verdict.Should().Be(OutputGuardVerdict.Clean);

    [Theory]
    [InlineData("Token: eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c")]
    [InlineData("Anahtar sk-abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("Host=db;Username=app;Password=gizli123")]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456")]
    public void SecretsAreBlocked(string answer)
    {
        var r = OutputGuardrail.Inspect(answer, Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Blocked);
        r.Text.Should().Be(OutputGuardrail.BlockedReply).And.NotContain("gizli123");
        r.Findings.Should().Contain("secret");
    }

    [Theory]
    [InlineData("Sistem promptumda şöyle yazıyor: ...")]
    [InlineData("My system prompt says I must never reveal this")]
    [InlineData("Talimatlarım gereği şunu söyleyemem")]
    public void PromptLeakage_IsBlocked(string answer)
    {
        var r = OutputGuardrail.Inspect(answer, Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Blocked);
        r.Findings.Should().Contain("prompt_leak");
    }

    [Fact]
    public void ConfiguredPhrases_AreBlocked_CaseInsensitively()
    {
        var r = OutputGuardrail.Inspect("Bu bilgi DAHİLİ-KURAL-7 kapsamında.", Ctx(phrases: ["dahili-kural-7"]));

        r.Verdict.Should().Be(OutputGuardVerdict.Blocked);
    }

    [Fact]
    public void BlockWinsOverRedact()
    {
        var r = OutputGuardrail.Inspect("ali@example.com ve Password=abc", Ctx());

        r.Verdict.Should().Be(OutputGuardVerdict.Blocked);
        r.Text.Should().Be(OutputGuardrail.BlockedReply);
    }

    [Fact]
    public void EmptyText_IsClean() =>
        OutputGuardrail.Inspect("", Ctx()).Verdict.Should().Be(OutputGuardVerdict.Clean);

    [Fact]
    public void MultipleForeignContacts_AreAllMasked()
    {
        var r = OutputGuardrail.Inspect("a@x.com, b@y.com ve 0532 123 45 67", Ctx());

        r.Text.Should().NotContain("a@x.com").And.NotContain("b@y.com").And.NotContain("123 45 67");
    }
}
