// Adapters.Agents/TrimmingDeltaStreamer.cs
// Akış hâlindeki metni, tamamı beklenmeden Trim()'lenmiş gibi yayınlamayı sağlar.

using System.Text;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Parça parça gelen bir metni, sonunda <see cref="string.Trim()"/> uygulanmış hâliyle
/// <b>birebir aynı</b> olacak şekilde ilerlemeli olarak yayınlar.
///
/// <para>
/// <b>Neden gerekli:</b> <c>DecomposedRunner</c> sıralı alt görevlerde gerçek LLM token
/// akışını kullanıcıya canlı iletir, ama alt görevin nihai metni
/// <c>SubTaskOrchestrator.FormatSubTaskResult</c> içinde <c>Trim()</c>'lenir. Ham token'lar
/// olduğu gibi iletilseydi, akan metin ile <c>response_complete</c>'teki nihai metin baştaki/
/// sondaki boşluk kadar ayrışırdı — ve bu ayrışma, ilerlemeli yayının en güçlü güvencesini
/// (birleşimin nihai metne eşitliği) bozardı.
/// </para>
///
/// <para>
/// <b>Nasıl çalışır:</b> ilk içerikten önceki boşluklar atılır; içerikten sonraki boşluklar
/// <i>sondaki boşluk olabilir</i> diye tutulur ve ancak arkasından yeni bir içerik geldiğinde
/// yayınlanır. Akış bittiğinde elde tutulan boşluk hiç yayınlanmaz — sonuç tam olarak
/// <c>Trim()</c>'dir. Aynı "geri tutma" fikri <c>WorkflowTraceEventProcessor.ResponseStreamFilter</c>'da
/// da kullanılıyor (orada marker'ın parça sınırında bölünmesine karşı).
/// </para>
/// </summary>
internal sealed class TrimmingDeltaStreamer
{
    private readonly StringBuilder _heldWhitespace = new();
    private bool _sawContent;

    /// <summary>
    /// Yeni parçayı işler ve <b>şimdi yayınlanması güvenli olan</b> metni döner (boş olabilir).
    /// Akış bittiğinde ayrıca bir şey çağırmak gerekmez: elde kalan boşluk kasıtlı olarak düşer.
    /// </summary>
    public string Feed(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return "";

        var output = new StringBuilder();
        foreach (var c in chunk)
        {
            if (char.IsWhiteSpace(c))
            {
                // İçerik görülmeden önceki boşluk baştaki boşluktur — atılır.
                // Sonrasındaki boşluk sondaki OLABİLİR — arkasından içerik gelirse yayınlanır.
                if (_sawContent) _heldWhitespace.Append(c);
                continue;
            }

            if (_heldWhitespace.Length > 0)
            {
                output.Append(_heldWhitespace);
                _heldWhitespace.Clear();
            }
            output.Append(c);
            _sawContent = true;
        }

        return output.ToString();
    }
}
