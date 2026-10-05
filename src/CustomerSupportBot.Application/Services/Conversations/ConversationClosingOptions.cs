// Application/Services/Conversations/ConversationClosingOptions.cs
// appsettings "ConversationClosing": kapanış nedenleri ve zorunluluk.

using CustomerSupportBot.Application.Ports.Inbound;

namespace CustomerSupportBot.Application.Services.Conversations;

public sealed class ConversationClosingOptions
{
    public const string SectionName = "ConversationClosing";

    /// <summary>Panelden kapatırken neden zorunlu mu.</summary>
    public bool RequireReason { get; set; } = true;

    /// <summary>
    /// Yapılandırılmış nedenler. Varsayılan liste burada değil <see cref="DefaultReasons"/>'ta durur:
    /// yapılandırma bağlayıcısı listeleri mevcut öğelerin <b>üstüne ekler</b>; varsayılanlar burada olsaydı
    /// appsettings'teki liste onların yerine geçmez, sonlarına eklenirdi.
    /// </summary>
    public List<ClosingReasonSetting> Reasons { get; set; } = [];

    public static IReadOnlyList<ClosingReasonOption> DefaultReasons { get; } =
    [
        new("resolved", "Çözüldü"),
        new("information_provided", "Bilgi verildi"),
        new("follow_up_required", "Takip gerekiyor"),
        new("customer_unresponsive", "Müşteri yanıt vermedi"),
        new("transferred", "Başka birime yönlendirildi"),
        new("other", "Diğer")
    ];

    /// <summary>Yapılandırılmış nedenler; hiç yoksa varsayılanlar. Kodu boş olanlar atılır.</summary>
    public IReadOnlyList<ClosingReasonOption> EffectiveReasons
    {
        get
        {
            var configured = Reasons
                .Where(r => !string.IsNullOrWhiteSpace(r.Code))
                .Select(r => new ClosingReasonOption(r.Code.Trim(), string.IsNullOrWhiteSpace(r.Label) ? r.Code.Trim() : r.Label.Trim()))
                .ToList();
            return configured.Count > 0 ? configured : DefaultReasons;
        }
    }
}

public sealed class ClosingReasonSetting
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
}
