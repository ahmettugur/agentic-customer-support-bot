// Models/VoiceCallModel.cs
// Sesli görüşme ekranının durumu — realtime-ui.js olaylarından beslenir, VoiceCallOverlay çizer.

namespace CustomerSupportBot.Web.Models;

/// <summary>Kürenin görünümü. Öncelik: Error > Handoff > Speaking > Muted > Connecting > Thinking > Listening.</summary>
public enum VoiceOrbState { Connecting, Listening, Thinking, Speaking, Muted, Handoff, Error }

/// <summary>
/// Görüşme ekranının tek durum kaynağı. JS (realtime-ui.js) her olayı <see cref="Apply"/> ile
/// iletir; bileşen yalnızca okur. Saf sınıf — tarayıcı olmadan birim testlenir.
///
/// <para>
/// <b>Altyazı yalnızca son tur:</b> dökümün tamamı zaten sohbete balon olarak yazılıyor. Yeni bir
/// kullanıcı konuşması (yeni itemId) önceki yanıtı ekrandan kaldırır; canlı parçası olmayan
/// modellerde ise biten bir yanıttan sonra gelen ilk asistan parçası eskisinin yerine geçer.
/// Kullanıcının son transkripti asistan yanıtından SONRA gelebilir (sağlayıcı sırası); eski bir turun
/// geç gelen son transkripti yeni turun altyazısının üzerine yazılmaz. Canlı parçası olmayan modellerde
/// turun kimliği <c>speech_stopped</c>'tan gelir — eskiden yalnızca canlı parçadan geliyordu ve altyazı bir
/// tur geriden (önceki cümleyle) kalıyordu.
/// </para>
///
/// <para>
/// <b>Aktarım durumu kalıcı değildir:</b> küre, temsilci talebi oluşturulup duyurulana (ya da müşteri yeniden
/// konuşana) kadar "Temsilciye aktarılıyor" gösterir; görüşme sürerse normal durumlara döner (eskiden
/// görüşmenin sonuna kadar takılı kalıyordu). Bilgi çipte kalır.
/// </para>
/// </summary>
public sealed class VoiceCallModel
{
    public const string HandoffTool = "human_handoff_tool";

    private static readonly HashSet<string> SideEffectTools =
    [
        "order_placement_tool", "order_cancel_tool", "return_request_tool", "complaint_registration_tool"
    ];

    private string _clientState = "connecting";
    private bool _thinking;
    private bool _handoff;
    private bool _handoffCreated;
    private bool _assistantTurnDone;
    private string? _userItemId;
    private DateTimeOffset? _connectedAt;

    public bool Open { get; private set; }
    public bool Muted { get; private set; }
    public string? UserCaption { get; private set; }
    public bool UserCaptionLive { get; private set; }
    public string AssistantCaption { get; private set; } = "";
    public string? Chip { get; private set; }
    public string? Error { get; private set; }
    public string? EndedMessage { get; private set; }

    public VoiceOrbState Orb =>
        Error is not null ? VoiceOrbState.Error
        : _handoff ? VoiceOrbState.Handoff
        : _clientState == "speaking" ? VoiceOrbState.Speaking
        : Muted ? VoiceOrbState.Muted
        : _clientState == "connecting" ? VoiceOrbState.Connecting
        : _thinking ? VoiceOrbState.Thinking
        : VoiceOrbState.Listening;

    public string StateLabel => Orb switch
    {
        VoiceOrbState.Connecting => "Bağlanıyor…",
        VoiceOrbState.Listening => "Dinliyor",
        VoiceOrbState.Thinking => "Düşünüyor",
        VoiceOrbState.Speaking => "Konuşuyor",
        VoiceOrbState.Muted => "Mikrofon kapalı",
        VoiceOrbState.Handoff => "Temsilciye aktarılıyor",
        _ => Error ?? "Hata"
    };

    public string OrbCssClass => Orb.ToString().ToLowerInvariant();

    /// <summary>Bağlantıdan bu yana geçen süre, "mm:ss" (60 dakikayı aşınca dakika büyür).</summary>
    public string Elapsed(DateTimeOffset now)
    {
        if (_connectedAt is not { } start || now < start) return "00:00";
        var t = now - start;
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    public void Apply(string type, string? a, string? b, DateTimeOffset now)
    {
        switch (type)
        {
            case "open":
                Reset();
                Open = true;
                break;
            case "connected":
                _connectedAt ??= now;
                break;
            case "state":
                // İstemcinin "error"/"idle" durumu bir görünüm değildir: hata ayrıca 'error' ya da
                // 'notice' olayıyla gelir, kapanış 'close' ile. Yoksa küre düşüp "Dinliyor" derdi.
                if (a is null or "error" or "idle") break;
                _clientState = a;
                if (_clientState == "speaking") _thinking = false;
                if (_clientState == "listening") _connectedAt ??= now;
                break;
            case "speech_started":
                _thinking = false;
                if (_handoffCreated) _handoff = false;
                break;
            case "speech_stopped":
                _thinking = true;
                if (a is not null && a != _userItemId) StartUserTurn(a);
                break;
            case "user_delta":
                if (a != _userItemId) StartUserTurn(a);
                UserCaption += b ?? "";
                UserCaptionLive = true;
                break;
            case "user_final":
                if (a is null || _userItemId is null || a == _userItemId)
                {
                    UserCaption = b;
                    UserCaptionLive = false;
                }
                break;
            case "assistant_delta":
                if (_assistantTurnDone)
                {
                    AssistantCaption = "";
                    _assistantTurnDone = false;
                }
                AssistantCaption += a ?? "";
                _thinking = false;
                break;
            case "response_done":
                _assistantTurnDone = true;
                _thinking = false;
                if (_handoffCreated) _handoff = false;
                break;
            case "tool_call":
                _thinking = true;
                if (a == HandoffTool) _handoff = true;
                Chip = (a == HandoffTool || (a is not null && SideEffectTools.Contains(a)) ? "⏳ " : "🔎 ") + (b ?? a);
                break;
            case "tool_result":
                if (a == HandoffTool) _handoffCreated = true;
                Chip = a == HandoffTool ? "👤 Temsilci talebi oluşturuldu"
                    : a is not null && SideEffectTools.Contains(a) ? "⏳ Talebiniz onaya gönderildi"
                    : null;
                break;
            case "notice":
                // Görüşmeyi bitirmeyen sunucu uyarısı (ör. girdi reddedildi) — çip olarak gösterilir.
                Chip = "⚠ " + (string.IsNullOrWhiteSpace(a) ? "Mesaj işlenemedi." : a);
                break;
            case "muted":
                Muted = a == "true";
                break;
            case "error":
                Error = string.IsNullOrWhiteSpace(a) ? "Bir hata oluştu." : a;
                break;
            case "ended":
                EndedMessage = a;
                break;
            case "close":
                Open = false;
                break;
        }
    }

    /// <summary>Yeni kullanıcı turu: önceki turun altyazıları ekrandan kalkar.</summary>
    private void StartUserTurn(string? itemId)
    {
        _userItemId = itemId;
        UserCaption = "";
        UserCaptionLive = false;
        AssistantCaption = "";
        _assistantTurnDone = false;
    }

    private void Reset()
    {
        _clientState = "connecting";
        _thinking = _handoff = _handoffCreated = _assistantTurnDone = false;
        _userItemId = null;
        _connectedAt = null;
        Muted = false;
        UserCaption = null;
        UserCaptionLive = false;
        AssistantCaption = "";
        Chip = Error = EndedMessage = null;
    }
}
