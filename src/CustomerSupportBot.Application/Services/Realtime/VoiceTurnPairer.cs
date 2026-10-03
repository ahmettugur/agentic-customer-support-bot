// Application/Services/Realtime/VoiceTurnPairer.cs
// Native sesli modda kullanıcı transkriptini, onu yanıtlayan asistan metniyle eşleştirir.

using System.Text;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>Geçmişe yazılmaya hazır bir sesli tur.</summary>
internal readonly record struct VoiceTurn(string UserSide, string BotText);

/// <summary>
/// Native sesli modda bir turun iki yarısını — kullanıcının söylediği ve asistanın yanıtı —
/// doğru şekilde eşleştirir ve turları geçmişe <b>sırayla</b> verir.
///
/// <para>
/// <b>Neden gerekli:</b> OpenAI Realtime'da kullanıcı sesinin transkripsiyonu yanıt üretimiyle
/// paralel çalışır; transkript yanıt olaylarından önce de, ortasında da, <c>response.done</c>'dan
/// sonra da gelebilir. Servis eskiden yanıtı "o ana kadar gelen son transkript"le eşliyordu:
/// transkript geç geldiğinde tur bir önceki kullanıcı cümlesiyle (ya da <c>(sesli)</c> ile)
/// kaydediliyor, kural tabanlı duygu çıkarımı da yanlış metin üzerinde çalışıyordu.
/// </para>
///
/// <para>
/// <b>Eşleştirme anahtarı protokolün kendi bağıdır:</b> transkript, ait olduğu kullanıcı ses
/// öğesinin kimliğini (<c>item_id</c>) taşır; bir yanıt, başladığı anda son commit edilmiş
/// kullanıcı öğesine aittir. Tool sonucu üzerine başlayan takip yanıtı aynı kullanıcı turuna
/// aittir. Kimlik taşımayan bir akışta (kimliksiz adaptör, oturum başı karşılama yanıtı) eski
/// davranış korunur: son transkript ya da yer tutucu.
/// </para>
///
/// <para>
/// Turlar geldikleri sırayla verilir: transkripti henüz gelmemiş bir turun arkasındaki turlar
/// bekler, böylece geçmişte sıra bozulmaz. Transkript hiç gelmezse (olay kaybı) tur en fazla
/// <see cref="MaxPendingTurns"/> tur bekletilir, sonra yer tutucuyla yazılır; bağlantı
/// kapanınca bekleyenlerin hepsi <see cref="DrainAll"/> ile yazılır.
/// </para>
///
/// Tek bir olay döngüsünden çağrılır; thread-safe değildir.
/// </summary>
internal sealed class VoiceTurnPairer
{
    /// <summary>Transkripti olmayan turun kullanıcı tarafı.</summary>
    internal const string Placeholder = "(sesli)";

    /// <summary>Başı transkript bekleyen kuyruğun en fazla bu kadar turu bekletmesine izin verilir.</summary>
    internal const int MaxPendingTurns = 3;

    private const int MaxRememberedItems = 32;

    private sealed class PendingTurn(string? itemId)
    {
        public string? ItemId { get; } = itemId;
        public string? UserSide { get; set; }
        public StringBuilder BotText { get; } = new();
    }

    private readonly List<PendingTurn> _queue = new();
    private readonly Dictionary<string, string> _transcripts = new();
    private readonly HashSet<string> _rejected = new();
    private readonly Queue<string> _remembered = new();

    private string? _lastCommittedItemId;
    private string? _currentResponseItemId;
    private bool _awaitingToolFollowUp;

    /// <summary>Kullanıcı sesi bir konuşma öğesi olarak işlendi.</summary>
    public void AudioCommitted(string? itemId)
    {
        if (!string.IsNullOrEmpty(itemId)) _lastCommittedItemId = itemId;
    }

    /// <summary>
    /// Yeni bir yanıt başladı. Tool sonucu üzerine gelen takip yanıtıysa <c>true</c> döner —
    /// o yanıt aynı kullanıcı turuna aittir.
    /// </summary>
    public bool ResponseCreated()
    {
        if (_awaitingToolFollowUp)
        {
            _awaitingToolFollowUp = false;
            return true;
        }

        _currentResponseItemId = _lastCommittedItemId;
        return false;
    }

    /// <summary>Yanıt tool çağrılarıyla bitti; sonuçlar gönderiliyor.</summary>
    public void ToolCallsDispatched(bool followUpExpected) => _awaitingToolFollowUp = followUpExpected;

    public void ResponseCancelled() => _awaitingToolFollowUp = false;

    /// <summary>
    /// Şu an üretilen yanıtın ait olduğu kullanıcı cümlesi (onay kaydı / eskalasyon için).
    /// Eşleştirme kimliği yoksa <paramref name="legacy"/> (son transkript) döner; kimlik var ama
    /// transkript henüz gelmediyse <c>null</c> — bir önceki turun cümlesi yanlışlıkla yazılmasın.
    /// </summary>
    public string? CurrentTranscript(string? legacy) =>
        _currentResponseItemId is null
            ? legacy
            : _transcripts.GetValueOrDefault(_currentResponseItemId);

    /// <summary>
    /// Bir yanıt metniyle tamamlandı. <paramref name="legacyUserSide"/>, eşleştirme kimliği
    /// olmayan akışta kullanılacak kullanıcı tarafıdır (son transkript).
    /// </summary>
    public IReadOnlyList<VoiceTurn> ResponseCompleted(string botText, string? legacyUserSide)
    {
        var itemId = _currentResponseItemId;
        if (itemId is null)
        {
            var legacy = new PendingTurn(null) { UserSide = legacyUserSide ?? Placeholder };
            legacy.BotText.Append(botText);
            _queue.Add(legacy);
            return DrainResolved();
        }

        if (_rejected.Contains(itemId)) return [];

        var turn = new PendingTurn(itemId)
        {
            UserSide = _transcripts.TryGetValue(itemId, out var transcript) ? transcript : null
        };
        turn.BotText.Append(botText);
        _queue.Add(turn);
        return DrainResolved();
    }

    /// <summary>Bir kullanıcı öğesinin transkripti geldi.</summary>
    public IReadOnlyList<VoiceTurn> TranscriptArrived(string? itemId, string transcript)
    {
        if (string.IsNullOrEmpty(itemId) || _rejected.Contains(itemId)) return [];

        _transcripts[itemId] = transcript;
        Remember(itemId);
        foreach (var turn in _queue)
        {
            if (turn.ItemId == itemId && turn.UserSide is null)
                turn.UserSide = transcript;
        }
        return DrainResolved();
    }

    /// <summary>Transkripsiyon başarısız oldu ya da anlaşılır konuşma yoktu.</summary>
    public IReadOnlyList<VoiceTurn> TranscriptFailed(string? itemId) => TranscriptArrived(itemId, Placeholder);

    /// <summary>
    /// Transkript girdi güvenliğince reddedildi: o tura ait yanıt geçmişe YAZILMAZ — reddedilen
    /// metin sonraki turlarda ajanın bağlamına girmemeli.
    /// </summary>
    public IReadOnlyList<VoiceTurn> TranscriptRejected(string? itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return [];

        _rejected.Add(itemId);
        _transcripts.Remove(itemId);
        Remember(itemId);
        _queue.RemoveAll(t => t.ItemId == itemId);
        return DrainResolved();
    }

    /// <summary>Bağlantı kapanıyor: bekleyen her tur, transkripti yoksa yer tutucuyla verilir.</summary>
    public IReadOnlyList<VoiceTurn> DrainAll()
    {
        var all = _queue.Select(t => new VoiceTurn(t.UserSide ?? Placeholder, t.BotText.ToString())).ToList();
        _queue.Clear();
        return all;
    }

    private List<VoiceTurn> DrainResolved()
    {
        var ready = new List<VoiceTurn>();
        while (_queue.Count > 0)
        {
            var head = _queue[0];
            if (head.UserSide is null)
            {
                // Başın transkripti hâlâ yok. Arkasında birikenler çok uzadıysa transkript
                // kaybolmuş sayılır — geçmiş süresiz askıda kalmasın.
                if (_queue.Count <= MaxPendingTurns) break;
                head.UserSide = Placeholder;
            }

            _queue.RemoveAt(0);
            ready.Add(new VoiceTurn(head.UserSide, head.BotText.ToString()));
        }
        return ready;
    }

    private void Remember(string itemId)
    {
        _remembered.Enqueue(itemId);
        while (_remembered.Count > MaxRememberedItems)
        {
            var old = _remembered.Dequeue();
            if (_remembered.Contains(old)) continue;
            _transcripts.Remove(old);
            _rejected.Remove(old);
        }
    }
}
