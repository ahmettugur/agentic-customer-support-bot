// Ports/Outbound/VoiceEngineOptions.cs
// Sesli kanalı hangi motorun yürüteceği ve GPT-Live motorunun ayarları.

namespace CustomerSupportBot.Application.Ports.Outbound;

public enum VoiceEngine
{
    /// <summary><c>gpt-realtime-*</c>: yarı çift yönlü; model konuşur, karar verir ve tool'ları kendisi çağırır.</summary>
    Realtime,

    /// <summary><c>gpt-live-*</c>: tam çift yönlü; model konuşmayı yürütür, tool ve akıl yürütmeyi arka plan modeline devreder.</summary>
    Live
}

/// <summary>
/// <c>AI:Realtime:Engine</c>. Her sesli bağlantıda okunur: ayar değişince yeni görüşmeler yeni motorla başlar, uygulama yeniden
/// başlatılmaz, süren görüşmeler etkilenmez. Geçersiz değer açılışta hata verir.
/// </summary>
public sealed class VoiceEngineOptions
{
    public const string SectionName = "AI:Realtime";

    public VoiceEngine Engine { get; set; } = VoiceEngine.Realtime;
}

/// <summary><c>AI:Realtime:Live</c> — yalnızca <see cref="VoiceEngine.Live"/> seçiliyken kullanılır.</summary>
public sealed class LiveVoiceOptions
{
    public const string SectionName = "AI:Realtime:Live";

    /// <summary>AB veri yerleşimi için <c>wss://eu.api.openai.com/v1/live/sessions</c> verilebilir.</summary>
    public string Endpoint { get; set; } = "wss://api.openai.com/v1/live/sessions";

    public string Model { get; set; } = "gpt-live-1";
    public string Voice { get; set; } = "marin";

    /// <summary>Tool seçimini ve akıl yürütmeyi yapan Responses modeli; ses modelinden bağımsızdır.</summary>
    public string BackendModel { get; set; } = "gpt-6-luna";

    /// <summary>Sesli yanıt beklediği için düşük tutulur; boşsa modelin varsayılanı.</summary>
    public string? BackendReasoningEffort { get; set; } = "low";

    /// <summary><c>auto</c>, <c>default</c>, <c>flex</c>, <c>priority</c>; boşsa proje ayarı.</summary>
    public string? BackendServiceTier { get; set; }

    /// <summary>Ses oturumunun dakika ücreti (saniye bazında işlenir). Arka plan modeli ayrıca fiyatlanır.</summary>
    public decimal PricePerMinuteUsd { get; set; } = 0.05m;

    /// <summary>Oturuma başlangıçta verilen son yazılı/sesli mesaj sayısı (sağlayıcı sınırı 128 mesaj, 8.192 token).</summary>
    public int HistoryMessages { get; set; } = 12;

    /// <summary>
    /// Bu süre yeni kullanıcı parçası gelmezse kullanıcının konuşması bitmiş sayılır (sağlayıcı tur sonu bildirmez). Parçalar düzensiz gelir
    /// (ölçüldü: 1 sn üstü boşluk); kısa tutulursa tek cümle iki balona bölünür. Yalnızca balonu etkiler, modelin yanıt süresini etkilemez.
    /// </summary>
    public int UserPauseMs { get; set; } = 1500;

    /// <summary>Bu süre asistan sesi ya da metni gelmezse ve arka plan işi yoksa asistanın turu bitmiş sayılır.</summary>
    public int AssistantPauseMs { get; set; } = 1800;

    /// <summary>
    /// Arka plan bittikten sonra modelin cevabı söylemeye başlaması için beklenen en uzun süre. Model "Bakıyorum" ile cevap arasında
    /// <see cref="AssistantPauseMs"/>'ten uzun susabiliyor (ölçüldü); bu süre içinde tur açık kalır, cevap ayrı tur olmaz.
    /// </summary>
    public double SpokenAnswerWaitSeconds { get; set; } = 6;

    /// <summary>
    /// Kimse konuşmadan ve arka plan işi yokken görüşme bu süre sonra kapanır. Live sessizliği de ücretlendirdiği için kapalı olamaz;
    /// tarayıcı sürekli ses gönderdiğinden etkinlik sesten değil konuşma metninden ölçülür.
    /// </summary>
    public double IdleTimeoutSeconds { get; set; } = 60;

    /// <summary>Görüşmenin üst süresi: açık unutulan bir bağlantı sınırsız ücretlenmesin.</summary>
    public double MaxSessionSeconds { get; set; } = 1800;

    /// <summary>Görüşme sonlandırma kararından sonra vedanın duyulması için en fazla beklenen süre.</summary>
    public double EndGraceSeconds { get; set; } = 6;
}
