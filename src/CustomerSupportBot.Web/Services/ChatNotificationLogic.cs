using System.Text.Json;

namespace CustomerSupportBot.Web.Services;

public sealed record ChatNotification(string Kind, string Title, string Text, string? ReferenceId);

/// <summary>
/// Sunucudan gelen sohbet içi bildirimin (SSE <c>notification</c>) saf mantığı: ayrıştırma + gösterim metni.
/// Metin <c>textContent</c> olarak (Razor kaçışlı) gösterilir; kargo firması gibi yönetici girdisi HTML olarak yorumlanmaz.
/// </summary>
public static class ChatNotificationLogic
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ChatNotification? Parse(string data)
    {
        try { return Parse(JsonSerializer.Deserialize<JsonElement>(data)); }
        catch (JsonException) { return null; }
    }

    public static ChatNotification? Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;
        try
        {
            var n = payload.Deserialize<ChatNotification>(Json);
            return n is { Kind.Length: > 0 } && !(string.IsNullOrWhiteSpace(n.Title) && string.IsNullOrWhiteSpace(n.Text)) ? n : null;
        }
        catch (JsonException) { return null; }
    }

    public static string Icon(string kind) => kind switch
    {
        "order_shipped" => "🚚",
        "order_delivered" => "📦",
        "complaint_in_review" => "🔎",
        "complaint_closed" => "✅",
        _ => "🔔"
    };

    public static string ChatNote(ChatNotification n) => $"{Icon(n.Kind)} {n.Title} — {n.Text}";

    public static string ToastText(ChatNotification n) => ChatNote(n);
}
