// Application/Services/Chat/ReasoningAnalysisStreamer.cs
// Reasoning LLM'i ham JSON akıtır ({"analysis":"…","nextAction":"…",…}). Müşteriye yalnızca "analysis" alanının METNİ gitmeli:
// ham parçalar iç alanları (nextAction, rationale…) tarayıcıya açardı.

using System.Text;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Parça parça gelen reasoning JSON'undan <c>analysis</c> değerini artımlı olarak çözüp döner. Parça sınırlarından bağımsızdır
/// (kaçış dizileri ve \uXXXX yarım gelirse bekletilir); kapanış tırnağından sonra hiçbir şey yayınlamaz.
/// </summary>
public sealed partial class ReasoningAnalysisStreamer
{
    private readonly StringBuilder _buffer = new();
    private int _position;
    private bool _started;
    private bool _done;

    /// <summary>Yeni ham parçayı işler; ŞİMDİ yayınlanabilir çözülmüş analiz metnini döner (boş olabilir).</summary>
    public string Feed(string? rawChunk)
    {
        if (_done || string.IsNullOrEmpty(rawChunk)) return "";
        _buffer.Append(rawChunk);

        if (!_started)
        {
            var m = AnalysisKey().Match(_buffer.ToString());
            if (!m.Success) return "";
            _position = m.Index + m.Length;
            _started = true;
        }

        var text = _buffer.ToString();
        var output = new StringBuilder();
        while (!_done && _position < text.Length)
        {
            var c = text[_position];
            if (c == '"') { _done = true; _position++; break; }

            if (c != '\\') { output.Append(c); _position++; continue; }

            if (_position + 1 >= text.Length) break;          // kaçış yarım: sonraki parçayı bekle
            var next = text[_position + 1];
            if (next == 'u')
            {
                if (_position + 6 > text.Length) break;       // \uXXXX yarım
                output.Append(int.TryParse(text.AsSpan(_position + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var code)
                    ? (char)code : '?');
                _position += 6;
                continue;
            }

            output.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => next });
            _position += 2;
        }
        return output.ToString();
    }

    [GeneratedRegex(@"""analysis""\s*:\s*""")]
    private static partial Regex AnalysisKey();
}
