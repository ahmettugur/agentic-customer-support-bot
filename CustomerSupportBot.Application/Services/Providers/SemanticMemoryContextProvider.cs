// Application/Services/Providers/SemanticMemoryContextProvider.cs
// IContextProvider — kullanıcının son sorusunu Knowledge ve Lessons koleksiyonlarında arar,
// en yakın chunk'ları context olarak agent zincirine enjekte eder.

using System.Text;

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class SemanticMemoryContextProvider : IContextProvider
{
    private readonly SemanticMemoryService _memory;
    private readonly IContextSanitizer _sanitizer;
    private readonly ILogger<SemanticMemoryContextProvider> _logger;

    public string Name => "SemanticMemory";
    public int Order => 7;

    public SemanticMemoryContextProvider(
        SemanticMemoryService memory,
        IContextSanitizer sanitizer,
        ILogger<SemanticMemoryContextProvider> logger)
    {
        _memory = memory;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        if (!_memory.Enabled) return null;

        // Kullanıcının BU turdaki mesajıyla aranır.
        //
        // Eskiden sorgu oturum geçmişinin son kullanıcı mesajından okunuyordu; ancak geçmiş
        // workflow bittikten SONRA yazıldığı için güncel mesaj o anda henüz orada olmuyordu.
        // Sonuç: ilk turda hiç retrieval yapılmıyor, sonraki turlarda arama bir önceki turun
        // sorusuyla yapılıyordu. Sorgu artık doğrudan parametre olarak geliyor.
        var query = currentQuery;
        if (string.IsNullOrWhiteSpace(query)) return null;

        try
        {
            // Sorgu BİR KEZ embed edilir, iki koleksiyonda da aynı vektörle aranır.
            // Eskiden iki ayrı SearchAsync çağrısı vardı ve her biri kendi içinde aynı metni
            // yeniden embed ediyordu — embedder'da cache olmadığı için tur başına iki
            // embedding çağrısı (ve iki kat maliyet) oluşuyordu.
            var queryVector = await _memory.EmbedQueryAsync(query, ct);
            if (queryVector.Length == 0) return null;

            var kbTask = _memory.SearchByVectorAsync(MemoryKind.Knowledge, queryVector, ct: ct);
            var lessonsTask = _memory.SearchByVectorAsync(MemoryKind.Lesson, queryVector, ct: ct);
            await Task.WhenAll(kbTask, lessonsTask);

            var kb = kbTask.Result;
            var lessons = lessonsTask.Result;
            if (kb.Count == 0 && lessons.Count == 0) return null;

            var sb = new StringBuilder();
            int budget = _memory.Options.Retrieval.MaxContextChars;

            if (kb.Count > 0)
            {
                sb.AppendLine("## 📚 İlgili Bilgi Tabanı (citation'lı kullanılmalı)");
                AppendHits(sb, kb, ref budget);
            }
            if (lessons.Count > 0 && budget > 200)
            {
                sb.AppendLine();
                sb.AppendLine("## 🎓 Geçmiş Derslerden Öğrenilenler");
                AppendHits(sb, lessons, ref budget);
            }

            _logger.LogDebug("SemanticMemory context: kb={KbCount} lessons={LessonsCount}", kb.Count, lessons.Count);
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic memory arama başarısız oldu, atlanıyor");
            return null;
        }
    }

    private void AppendHits(StringBuilder sb, IReadOnlyList<MemorySearchHit> hits, ref int budget)
    {
        foreach (var h in hits)
        {
            if (budget <= 100) break;
            var title = _sanitizer.Sanitize(h.Document.Title ?? h.Document.Source ?? "(kaynaksız)");
            var src = h.Document.Source ?? "?";
            // Önce sanitize, sonra bütçe kırpması, en sonda wrap — fence asla bölünmez.
            var snippet = _sanitizer.Sanitize(h.Document.Text);
            if (snippet.Length > Math.Min(budget - 80, 500)) snippet = snippet[..Math.Min(budget - 80, 500)] + "…";
            var wrapped = _sanitizer.WrapRetrieved(
                snippet.Replace("\n", " ").Trim(),
                h.Document.Kind.ToString().ToLowerInvariant());
            sb.AppendLine($"- **[{title}]** _(score={h.Score:F2}, kaynak={src})_");
            sb.AppendLine($"  {wrapped}");
            budget -= snippet.Length + title.Length + 20;
        }
    }
}
