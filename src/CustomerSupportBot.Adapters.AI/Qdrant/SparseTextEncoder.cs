// Adapters.AI/Qdrant/SparseTextEncoder.cs
// Hibrit aramanın sözcüksel tarafı: metni Qdrant sparse vektörüne çevirir.
//
// Qdrant self-hosted'ta BM25 çıkarımını (inference) sunucu yapmaz; belge tarafı TF ağırlıklarını biz üretiriz
// ve koleksiyondaki IDF modifier'ı (Modifier.Idf) IDF çarpanını sunucuda uygular. Böylece BM25'e eşdeğer bir
// skor elde edilir: nadir sözcükler yüksek, yaygın sözcükler düşük ağırlık alır.
//
// Türkçe için: İ/I/ı doğru indirgenir, aksanlar düşer (müşteriler "iade sureci" yazar), harf-yalnız sözcükler
// ilk 5 karakterine kırpılır (Türkçe eklemeli bir dil; "kargo/kargom/kargoyu" aynı kökte buluşsun diye klasik
// "F5" kök bulma). Sayılar (sipariş no) olduğu gibi kalır.

using System.Text;

namespace CustomerSupportBot.Adapters.AI.Qdrant;

internal readonly record struct SparseVec(uint[] Indices, float[] Values);

internal static class SparseTextEncoder
{
    private const int StemLength = 5;
    private const float K1 = 1.2f;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "ve", "ile", "bir", "bu", "su", "mi", "mu", "de", "da", "ben", "icin", "gibi", "ama", "veya", "ki", "ne",
        "ya", "en", "cok", "daha", "var", "yok", "ise", "olan", "ola"
    };

    /// <summary>Belge vektörü: sözcük başına doygun TF (k1=1.2); IDF'i Qdrant uygular.</summary>
    public static SparseVec EncodeDocument(string text)
    {
        var counts = Count(Tokenize(text));
        return ToVector(counts, tf => tf * (K1 + 1f) / (tf + K1));
    }

    /// <summary>Sorgu vektörü: her ayrık sözcük 1.0 (Qdrant'ın kendi BM25 yaklaşımıyla aynı).</summary>
    public static SparseVec EncodeQuery(string text)
    {
        var counts = Count(Tokenize(text));
        return ToVector(counts, _ => 1f);
    }

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return tokens;

        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) { current.Append(Fold(ch)); continue; }
            Flush(current, tokens);
        }
        Flush(current, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder current, List<string> tokens)
    {
        if (current.Length == 0) return;
        var token = current.ToString();
        current.Clear();

        if (token.Length < 2 || Stopwords.Contains(token)) return;
        // Sayı içeren sözcük (sipariş no, ürün kodu) kırpılmaz.
        var hasDigit = token.Any(char.IsDigit);
        tokens.Add(!hasDigit && token.Length > StemLength ? token[..StemLength] : token);
    }

    /// <summary>Küçük harfe indirger ve Türkçe karakterleri ASCII karşılığına katlar.</summary>
    private static char Fold(char ch) => ch switch
    {
        'İ' or 'I' or 'ı' => 'i',
        'Ç' or 'ç' => 'c',
        'Ğ' or 'ğ' => 'g',
        'Ö' or 'ö' => 'o',
        'Ş' or 'ş' => 's',
        'Ü' or 'ü' => 'u',
        _ => char.ToLowerInvariant(ch)
    };

    private static Dictionary<uint, int> Count(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<uint, int>();
        foreach (var token in tokens)
        {
            var index = Hash(token);
            counts[index] = counts.GetValueOrDefault(index) + 1;
        }
        return counts;
    }

    private static SparseVec ToVector(Dictionary<uint, int> counts, Func<int, float> weight)
    {
        var ordered = counts.OrderBy(kv => kv.Key).ToArray();
        return new SparseVec(ordered.Select(kv => kv.Key).ToArray(), ordered.Select(kv => weight(kv.Value)).ToArray());
    }

    /// <summary>FNV-1a 32 bit — süreçler ve sürümler arasında kararlı (string.GetHashCode değildir).</summary>
    private static uint Hash(string token)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(token))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return hash;
    }
}
