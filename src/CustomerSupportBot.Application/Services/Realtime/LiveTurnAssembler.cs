// Application/Services/Realtime/LiveTurnAssembler.cs
// GPT-Live transkript parçalarından konuşma turları çıkarır.

using System.Text;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary><see cref="LiveTurnAssembler"/> çıktısı: ekrana gidecek olaylar ve geçmişe yazılacak turlar.</summary>
internal abstract record LiveTurnSignal
{
    public sealed record UserStarted(string ItemId) : LiveTurnSignal;
    public sealed record UserDelta(string ItemId, string Text) : LiveTurnSignal;
    public sealed record UserCompleted(string ItemId, string Text) : LiveTurnSignal;
    public sealed record AssistantDelta(string Text) : LiveTurnSignal;
    public sealed record AssistantCompleted(string Text) : LiveTurnSignal;
    public sealed record TurnReady(VoiceTurn Turn) : LiveTurnSignal;
}

/// <summary>
/// GPT-Live iki tarafın da transkriptini parça parça verir; parçalarda öğe kimliği yoktur, "tur bitti" olayı yoktur ve taraflar aynı anda
/// konuşabilir. Geçmiş, sohbet balonları ve onay kaydı ise tur ister. Bu sınıf turları duraklamalardan çıkarır:
///
/// <list type="bullet">
/// <item>Kullanıcı konuşması, yeni parça gelmeden <c>userPauseMs</c> geçince biter.</item>
/// <item>Asistan turu, ses ya da metin gelmeden <c>assistantPauseMs</c> geçince ve arka plan işi yokken biter. "Bakıyorum" ile cevabı
/// arasındaki bekleme <see cref="BackendBusy"/> ile aynı turda kalır.</item>
/// <item>Asistan konuşurken kullanıcı söze girerse önceki tur kapanır, yeni tur açılır. Kullanıcı konuşurken gelen asistan metni
/// (ör. "hı hı") kullanıcı bitene kadar bekletilir ki balon sırası bozulmasın.</item>
/// <item>Yanıtsız kalan iki kullanıcı konuşması tek turda birleşir. Yanıtsız tek kelime geçmişe yazılmaz: GPT-Live sessiz mikrofonda
/// hayali transkript üretebiliyor ve bu metin ajan bağlamına girmemeli.</item>
/// </list>
///
/// Zaman saf bir değerdir (ms); saat tutmaz. Tek bir olay döngüsünden çağrılır, thread-safe değildir.
/// </summary>
internal sealed class LiveTurnAssembler(int userPauseMs, int assistantPauseMs)
{
    private const int MinWordsForUnansweredTurn = 2;

    private readonly StringBuilder _pendingUser = new();
    private readonly StringBuilder _segment = new();
    private readonly StringBuilder _bot = new();
    private readonly StringBuilder _held = new();

    private string? _segmentId;
    private long _lastUserMs;
    private long _lastAssistantMs;
    private int _segmentCounter;
    private bool _rejected;

    /// <summary>Arka plan modeli çalışıyor: cevap yolda, asistan turu bitmiş sayılmaz.</summary>
    public bool BackendBusy { get; set; }

    /// <summary>Şu anki turda kullanıcının söyledikleri (onay kaydı ve eskalasyon metni); yoksa null.</summary>
    public string? CurrentUserText
    {
        get
        {
            var text = Join(_pendingUser.ToString(), _segment.ToString());
            return text.Length == 0 ? null : text;
        }
    }

    /// <summary>Açık kullanıcı konuşmasının kelime sayısı (söze girme kararı için).</summary>
    public int OpenSegmentWords => _segmentId is null ? 0 : CountWords(_segment.ToString());

    public IReadOnlyList<LiveTurnSignal> UserDelta(string text, long nowMs)
    {
        var signals = new List<LiveTurnSignal>();
        if (_segmentId is null)
        {
            if (string.IsNullOrWhiteSpace(text)) return signals;

            if (HasBotText) FinishAssistant(signals);
            _rejected = false;
            _segmentId = $"live-u{++_segmentCounter}";
            signals.Add(new LiveTurnSignal.UserStarted(_segmentId));
        }

        _segment.Append(text);
        _lastUserMs = nowMs;
        signals.Add(new LiveTurnSignal.UserDelta(_segmentId, text));
        return signals;
    }

    public IReadOnlyList<LiveTurnSignal> AssistantDelta(string text, long nowMs)
    {
        _bot.Append(text);
        _lastAssistantMs = nowMs;
        if (_segmentId is not null)
        {
            _held.Append(text);
            return [];
        }
        return [new LiveTurnSignal.AssistantDelta(text)];
    }

    /// <summary>Asistan sesi çalıyor: metin bitmiş olsa da asistan hâlâ konuşuyor.</summary>
    public void AssistantSpeech(long nowMs) => _lastAssistantMs = nowMs;

    public IReadOnlyList<LiveTurnSignal> Tick(long nowMs)
    {
        var signals = new List<LiveTurnSignal>();
        if (_segmentId is not null && nowMs - _lastUserMs >= userPauseMs) CloseSegment(signals);
        if (_segmentId is null && HasBotText && !BackendBusy && nowMs - _lastAssistantMs >= assistantPauseMs)
            FinishAssistant(signals);
        return signals;
    }

    /// <summary>
    /// Girdi koruması turu reddetti: kullanıcının söyledikleri ve bu turun asistan metni geçmişe yazılmaz, ama ekranda görünür.
    /// </summary>
    public IReadOnlyList<LiveTurnSignal> RejectCurrentTurn()
    {
        var signals = new List<LiveTurnSignal>();
        if (_segmentId is not null)
        {
            signals.Add(new LiveTurnSignal.UserCompleted(_segmentId, _segment.ToString().Trim()));
            _segmentId = null;
            _segment.Clear();
            _held.Clear();
        }
        _pendingUser.Clear();
        _rejected = true;
        return signals;
    }

    /// <summary>Görüşme bitti: bekleyen her şey çıkar.</summary>
    public IReadOnlyList<LiveTurnSignal> DrainAll()
    {
        var signals = new List<LiveTurnSignal>();
        if (_segmentId is not null) CloseSegment(signals);

        if (HasBotText)
        {
            FinishAssistant(signals);
            return signals;
        }

        var unanswered = CurrentUserText;
        if (unanswered is not null && !_rejected && CountWords(unanswered) >= MinWordsForUnansweredTurn)
            signals.Add(new LiveTurnSignal.TurnReady(new VoiceTurn(unanswered, "")));
        _pendingUser.Clear();
        return signals;
    }

    private bool HasBotText => _bot.ToString().Trim().Length > 0;

    private void CloseSegment(List<LiveTurnSignal> signals)
    {
        var text = _segment.ToString().Trim();
        signals.Add(new LiveTurnSignal.UserCompleted(_segmentId!, text));
        var merged = Join(_pendingUser.ToString(), text);
        _pendingUser.Clear().Append(merged);
        _segment.Clear();
        _segmentId = null;

        if (_held.Length == 0) return;
        signals.Add(new LiveTurnSignal.AssistantDelta(_held.ToString()));
        _held.Clear();
    }

    private void FinishAssistant(List<LiveTurnSignal> signals)
    {
        var text = _bot.ToString().Trim();
        signals.Add(new LiveTurnSignal.AssistantCompleted(text));
        if (!_rejected)
        {
            var user = _pendingUser.Length == 0 ? VoiceTurnPairer.Placeholder : _pendingUser.ToString();
            signals.Add(new LiveTurnSignal.TurnReady(new VoiceTurn(user, text)));
        }
        _pendingUser.Clear();
        _bot.Clear();
    }

    private static string Join(string first, string second)
    {
        var a = first.Trim();
        var b = second.Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return char.IsPunctuation(b[0]) ? a + b : $"{a} {b}";
    }

    private static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
