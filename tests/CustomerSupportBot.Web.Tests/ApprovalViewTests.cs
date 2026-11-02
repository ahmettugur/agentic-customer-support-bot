// Onay kuyruğu metinleri. Kayıttaki kullanıcı metni tetikleyen turun cümlesidir: müşteri bir teyit sorusuna cevap verdiyse bu yalnızca
// "Evet, gönderebilirsiniz" olur ve temsilciye talebin ne olduğunu söylemez. Temsilci önce talebi görmeli.

using CustomerSupportBot.Web.Helpers;

namespace CustomerSupportBot.Web.Tests;

public class ApprovalViewTests
{
    [Theory]
    [InlineData("Evet, gönderebilirsiniz")]
    [InlineData("Onaylıyorum.")]
    [InlineData("ONAYLIYORUM")]
    [InlineData("evet")]
    [InlineData("Tamam, oluşturun lütfen")]
    [InlineData("Evet evet, aynen öyle")]
    [InlineData("İptal edin, onaylıyorum")]
    public void BareConfirmations_AreRecognized(string text)
    {
        ApprovalView.IsBareConfirmation(text).Should().BeTrue();
    }

    [Theory]
    [InlineData("Hayır, göndermeyin")]
    [InlineData("Evet ama adedi 2 olsun")]
    [InlineData("Onaylamıyorum")]
    [InlineData("Gönderme")]
    [InlineData("1057 Siparişimin gelmesi çok uzun sürdü")]
    [InlineData("Bozuk ürün geldi")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnythingElse_IsNotABareConfirmation(string? text)
    {
        ApprovalView.IsBareConfirmation(text).Should().BeFalse();
    }

    [Fact]
    public void QuoteLabel_NamesAConfirmationForWhatItIs()
    {
        ApprovalView.QuoteLabel("Evet, gönderebilirsiniz").Should().Be("Müşterinin onayı");
        ApprovalView.QuoteLabel("Bozuk ürün geldi").Should().Be("Müşterinin son mesajı");
    }

    [Fact]
    public void Summary_ForAnOrder_ListsWhatIsOrdered()
    {
        ApprovalView.Summary([("Çay", 3)], null, null, "Evet, gönderebilirsiniz").Should().Be("3 × Çay");
        ApprovalView.Summary([("Çay", 3), ("Kahve", 1)], null, null, null).Should().Be("3 × Çay, 1 × Kahve");
    }

    [Fact]
    public void Summary_ForALongOrder_StaysOnOneRow()
    {
        ApprovalView.Summary([("Çay", 3), ("Kahve", 1), ("Bira", 2), ("Şeker", 1)], null, null, null)
            .Should().Be("3 × Çay, 1 × Kahve +2 ürün");
    }

    [Fact]
    public void Summary_ForAnOrderAction_ShowsTheOrderAndTheReason()
    {
        ApprovalView.Summary(null, "1057", "Bozuk ürün geldi", "Onaylıyorum.").Should().Be("#1057 · Bozuk ürün geldi");
    }

    [Fact]
    public void Summary_WithoutAReason_StillShowsTheOrder()
    {
        ApprovalView.Summary(null, "1057", " ", null).Should().Be("#1057");
    }

    [Fact]
    public void Summary_ForAnUnknownTool_FallsBackToWhatTheCustomerSaid()
    {
        ApprovalView.Summary(null, null, null, "Adresimi güncellemek istiyorum").Should().Be("Adresimi güncellemek istiyorum");
    }

    [Fact]
    public void Summary_NeverFallsBackToABareConfirmation()
    {
        ApprovalView.Summary(null, null, null, "Evet, gönderebilirsiniz").Should().BeNull();
    }
}
