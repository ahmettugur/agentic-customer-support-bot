// Adapters.AI/Qdrant/ReciprocalRankFusion.cs
// Birden çok sıralamayı (dense, sparse) tek sıralamada birleştirir: skor = Σ 1 / (k + sıra).
// Skorlar farklı ölçeklerde olduğundan (kosinüs vs. BM25) ham skorlar toplanamaz; yalnızca sıra kullanılır.

namespace CustomerSupportBot.Adapters.AI.Qdrant;

internal static class ReciprocalRankFusion
{
    public const int DefaultK = 60;

    public static IReadOnlyList<(string Id, double Score)> Fuse(
        IReadOnlyList<IReadOnlyList<string>> rankedLists, int k, int take)
    {
        var scores = new Dictionary<string, double>();
        var firstSeen = new List<string>();

        foreach (var list in rankedLists)
        {
            var seenInList = new HashSet<string>();
            var rank = 0;
            foreach (var id in list)
            {
                if (!seenInList.Add(id)) continue;   // aynı listedeki tekrar yalnızca ilk sırasıyla sayılır
                rank++;
                if (!scores.ContainsKey(id)) firstSeen.Add(id);
                scores[id] = scores.GetValueOrDefault(id) + 1d / (k + rank);
            }
        }

        // OrderBy kararlıdır: eşit skorlarda ilk görülme sırası korunur (deterministik).
        return firstSeen.OrderByDescending(id => scores[id]).Take(take).Select(id => (id, scores[id])).ToList();
    }

    /// <summary>Her sinyalde 1. sırada olan belgenin skoru — ham RRF'yi 0–1'e ölçeklemek için.</summary>
    public static double MaxScore(int signals, int k) => signals / (double)(k + 1);
}
