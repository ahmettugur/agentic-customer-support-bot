// Sohbet içi bildirim olayının (SSE `notification`) ayrıştırılması ve gösterim kuralları.
// Sunucu JSON'u camelCase gönderir (CustomerNotification → Web varsayılanları).

using System.Text.Json;
using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class ChatNotificationLogicTests
{
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    [Fact]
    public void ParsesTheServerPayload()
    {
        var n = ChatNotificationLogic.Parse(Json(
            """{"kind":"order_shipped","title":"Siparişiniz kargoya verildi","text":"#1044 kargoda","referenceId":"1044","createdAt":"2026-10-21T09:00:00Z"}"""));

        n.Should().NotBeNull();
        n!.Kind.Should().Be("order_shipped");
        n.Title.Should().Be("Siparişiniz kargoya verildi");
        n.Text.Should().Be("#1044 kargoda");
        n.ReferenceId.Should().Be("1044");
    }

    [Theory]
    [InlineData("""{"kind":"order_shipped","title":"","text":""}""")]
    [InlineData("""{"title":"x","text":"y"}""")]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    public void AnUnusablePayload_IsIgnored(string raw) =>
        ChatNotificationLogic.Parse(Json(raw)).Should().BeNull();

    [Fact]
    public void ParsesFromTheRawSseDataString() =>
        ChatNotificationLogic.Parse("""{"kind":"order_delivered","title":"Teslim edildi","text":"#1 teslim"}""")!
            .Kind.Should().Be("order_delivered");

    [Fact]
    public void ABrokenDataString_IsIgnored() =>
        ChatNotificationLogic.Parse("{not json").Should().BeNull();

    [Theory]
    [InlineData("order_shipped", "🚚")]
    [InlineData("order_delivered", "📦")]
    [InlineData("complaint_in_review", "🔎")]
    [InlineData("complaint_closed", "✅")]
    [InlineData("something_new", "🔔")]
    public void EachKindHasAnIcon_AndUnknownKindsFallBack(string kind, string icon) =>
        ChatNotificationLogic.Icon(kind).Should().Be(icon);

    [Fact]
    public void TheChatNote_CombinesIconTitleAndText()
    {
        var n = new ChatNotification("order_shipped", "Kargoya verildi", "#1044 yolda", "1044");

        ChatNotificationLogic.ChatNote(n).Should().Be("🚚 Kargoya verildi — #1044 yolda");
    }

    [Fact]
    public void TheToast_IsTheSameLineAsTheChatNote()
    {
        var n = new ChatNotification("complaint_closed", "Şikayetiniz sonuçlandı", "ayrıntılar e-postada", "c-9");

        ChatNotificationLogic.ToastText(n).Should().Be(ChatNotificationLogic.ChatNote(n));
    }
}
