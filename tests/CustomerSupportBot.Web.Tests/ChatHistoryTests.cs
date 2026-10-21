// Müşterinin geçmiş konuşmaları: listeleme, açılışta geri yükleme kararı, zaman etiketi.
// Tarayıcı kültürü (WASM'de tr-TR ICU yok) bu etiketlerin biçimini DEĞİŞTİRMEMELİ — biçim elle üretilir.

using System.Text.Json;
using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class ChatHistoryTests
{
    private static SessionInfo S(string id, int messages, DateTimeOffset last, string title = "t") => new(id, title, last, messages);
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);

    // ═══ liste ═══

    [Fact]
    public void Visible_HidesEmptySessions_AndOrdersNewestFirst()
    {
        var list = ChatHistory.Visible([S("eski", 4, Now.AddDays(-3)), S("bos", 0, Now), S("yeni", 2, Now.AddHours(-1))]);

        list.Select(s => s.SessionId).Should().Equal("yeni", "eski");
    }

    [Fact]
    public void Visible_CapsTheList()
    {
        var many = Enumerable.Range(0, 100).Select(i => S($"s{i}", 2, Now.AddMinutes(-i))).ToList();

        ChatHistory.Visible(many).Should().HaveCount(ChatHistory.MaxListed);
        ChatHistory.Visible(many).First().SessionId.Should().Be("s0");
    }

    [Theory]
    [InlineData("", "Yeni sohbet")]
    [InlineData("   ", "Yeni sohbet")]
    [InlineData("Siparişim nerede?", "Siparişim nerede?")]
    public void TitleOf_FallsBackForBlank(string title, string expected) =>
        ChatHistory.TitleOf(S("s", 2, Now, title)).Should().Be(expected);

    // ═══ açılışta geri yükleme ═══

    [Fact]
    public void Restore_ReturnsTheRememberedSession_WhenItStillExistsAndHasMessages()
    {
        ChatHistory.ResolveRestore([S("a", 2, Now), S("b", 3, Now.AddDays(-1))], active: "b").Should().Be("b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("silinmis")]
    public void Restore_ReturnsNull_WhenNothingValidIsRemembered(string? active)
    {
        // Kullanıcı "Yeni sohbet"e basıp yenilediyse (hatırlanan yok) eski konuşma geri GELMEMELİ.
        ChatHistory.ResolveRestore([S("a", 2, Now)], active).Should().BeNull();
    }

    [Fact]
    public void Restore_IgnoresARememberedSessionWithNoMessages()
    {
        ChatHistory.ResolveRestore([S("bos", 0, Now)], active: "bos").Should().BeNull();
    }

    // ═══ roller ═══

    [Theory]
    [InlineData("user", "user")]
    [InlineData("bot", "assistant")]
    [InlineData("assistant", "assistant")]
    [InlineData("agent", "assistant")]
    [InlineData("", "assistant")]
    public void UiRole_MapsServerRoles(string server, string expected) => ChatHistory.UiRole(server).Should().Be(expected);

    // ═══ zaman etiketi ═══

    [Theory]
    [InlineData(0, 0, "Bugün 14:20")]      // aynı gün
    [InlineData(-1, 0, "Dün 14:20")]
    [InlineData(-3, 0, "3 gün önce")]
    [InlineData(-6, 0, "6 gün önce")]
    [InlineData(-9, 0, "12.10.2026")]
    public void RelativeTime_IsFormattedByHand(int dayOffset, int _, string expected)
    {
        var at = new DateTimeOffset(2026, 10, 21, 14, 20, 0, TimeSpan.Zero).AddDays(dayOffset);

        ChatHistory.RelativeTime(at, Now, TimeSpan.Zero).Should().Be(expected);
    }

    [Fact]
    public void RelativeTime_UsesTheBrowsersUtcOffsetForDayBoundaries()
    {
        // 22:30 UTC, tarayıcı UTC+3 → yerelde ertesi günün 01:30'u; "now" de yerelde ertesi gün.
        var at = new DateTimeOffset(2026, 10, 20, 22, 30, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);

        ChatHistory.RelativeTime(at, now, TimeSpan.FromHours(3)).Should().Be("Bugün 01:30");
    }

    // ═══ sunucu sözleşmesi (JSON şekli) ═══

    [Fact]
    public void Dtos_DeserializeTheShapeTheServerEmits()
    {
        var sessions = JsonSerializer.Deserialize<List<SessionInfo>>(
            """[{"sessionId":"s1","title":"Merhaba","lastActivity":"2026-10-21T12:00:00+00:00","messageCount":2}]""",
            JsonSerializerOptions.Web)!;
        var messages = JsonSerializer.Deserialize<List<SessionMessage>>(
            """[{"role":"user","text":"merhaba"},{"role":"bot","text":"selam"}]""", JsonSerializerOptions.Web)!;

        sessions.Single().Title.Should().Be("Merhaba");
        messages.Select(m => m.Text).Should().Equal("merhaba", "selam");
    }
}
