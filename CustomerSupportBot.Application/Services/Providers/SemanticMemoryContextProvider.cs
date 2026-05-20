// Application/Services/Providers/SemanticMemoryContextProvider.cs
// IContextProvider — kullanıcının son sorusunu Knowledge ve Lessons koleksiyonlarında arar,
// en yakın chunk'ları context olarak agent zincirine enjekte eder.

using System.Text;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class SemanticMemoryContextProvider : IContextProvider
{
    private readonly SemanticMemoryService _memory;
    private readonly ISessionManager _sessionRepository;
    private readonly ILogger<SemanticMemoryContextProvider> _logger;

    public string Name => "SemanticMemory";
    public int Order => 7;

    public SemanticMemoryContextProvider(
        SemanticMemoryService memory,
        ISessionManager sessionRepository,
        ILogger<SemanticMemoryContextProvider> logger)
    {
        _memory = memory;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(AgentSession session)
    {
        if (!_memory.Enabled) return null;

        var history = _sessionRepository.GetHistory(session.SessionId);
        var lastUserMsg = history.LastOrDefault(m => m.Role == ConversationRoles.User);
        var query = lastUserMsg?.Text;
        if (string.IsNullOrWhiteSpace(query)) return null;

        try
        {
            var kbTask = _memory.SearchAsync(MemoryKind.Knowledge, query);
            var lessonsTask = _memory.SearchAsync(MemoryKind.Lesson, query);
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

    private static void AppendHits(StringBuilder sb, IReadOnlyList<MemorySearchHit> hits, ref int budget)
    {
        foreach (var h in hits)
        {
            if (budget <= 100) break;
            var title = h.Document.Title ?? h.Document.Source ?? "(kaynaksız)";
            var src = h.Document.Source ?? "?";
            var snippet = h.Document.Text;
            if (snippet.Length > Math.Min(budget - 80, 500)) snippet = snippet[..Math.Min(budget - 80, 500)] + "…";
            sb.AppendLine($"- **[{title}]** _(score={h.Score:F2}, kaynak={src})_");
            sb.AppendLine($"  {snippet.Replace("\n", " ").Trim()}");
            budget -= snippet.Length + title.Length + 20;
        }
    }
}
