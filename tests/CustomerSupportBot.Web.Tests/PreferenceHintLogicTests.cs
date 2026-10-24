// Tercih onayı (ui_hint) müşteriye görünür sistem notuna çevrilir; başka ui_hint türleri not üretmez.

using System.Text.Json;
using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class PreferenceHintLogicTests
{
    private static JsonElement Json(string raw) => JsonSerializer.Deserialize<JsonElement>(raw);

    [Fact]
    public void ASavedPreference_NamesItAndExplainsHowToRemoveIt()
    {
        var note = PreferenceHintLogic.Message(Json("""{"kind":"preference_saved","key":"reply_style","value":"short","label":"kısa yanıtlar"}"""));

        note.Should().Contain("kısa yanıtlar").And.Contain("tercihlerimi sıfırla");
    }

    [Fact]
    public void AClearedNotice_IsShown() =>
        PreferenceHintLogic.Message(Json("""{"kind":"preferences_cleared"}""")).Should().Contain("silindi");

    [Theory]
    [InlineData("""{"kind":"suggested_replies","suggestions":["a"]}""")]
    [InlineData("""{"kind":"preference_saved"}""")]
    [InlineData("""{"kind":"preference_saved","label":""}""")]
    [InlineData("""{"agent":"OrderAgent"}""")]
    [InlineData("""[]""")]
    public void OtherHints_ProduceNoNote(string raw) => PreferenceHintLogic.Message(Json(raw)).Should().BeNull();
}
