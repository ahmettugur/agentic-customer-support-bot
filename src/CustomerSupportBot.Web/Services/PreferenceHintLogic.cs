using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Tercih onayı ui_hint'lerinin (<c>preference_saved</c>, <c>preferences_cleared</c>) müşteriye görünür sistem notuna çevrilmesi (saf).
/// Etiket sunucudaki kapalı sözlükten gelir; yine de metin Razor ile kaçışlı basılır.
/// </summary>
public static class PreferenceHintLogic
{
    public static string? Message(JsonElement hint)
    {
        if (hint.ValueKind != JsonValueKind.Object || !hint.TryGetProperty("kind", out var k) || k.ValueKind != JsonValueKind.String) return null;

        switch (k.GetString())
        {
            case "preference_saved":
                var label = hint.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null;
                return string.IsNullOrWhiteSpace(label)
                    ? null
                    : $"✅ Tercihiniz kaydedildi: {label}. Silmek için \"tercihlerimi sıfırla\" yazabilirsiniz.";
            case "preferences_cleared":
                return "✅ Kayıtlı tercihleriniz silindi.";
            default:
                return null;
        }
    }
}
