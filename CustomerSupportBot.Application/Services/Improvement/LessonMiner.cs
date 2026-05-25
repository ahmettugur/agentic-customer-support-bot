// Application/Services/Improvement/LessonMiner.cs
// Düşük puanlı veya hatalı trace'leri toplayıp LLM'e analiz ettirir;
// "Lesson" önerileri üretir. İdempotent değildir — çağıran admin onayı gerek.
//
// Heuristic seçim:
//   - rating ≤ MinRatingForLesson olan oturumların son trace'i
//   - termination_reason in {"error", "timeout"} olan trace'ler
//   - sanity issue varsa critical/error severity
//
// Çıktı JSON şeması:
//   { "lessons": [ { "title", "lesson", "observation", "suggestedAgent" } ] }

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Application.Ports.Driven.Observability;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Improvement;

public sealed class LessonMiner
{
    private readonly IReasoningTraceStore _traceStore;
    private readonly IRatingStore _ratingStore;
    private readonly ILessonStore _lessonStore;
    private readonly SemanticMemoryService? _memory;
    private readonly IGeneralChatClient _chatClient;
    private readonly SelfImprovementOptions _options;
    private readonly ILogger<LessonMiner> _logger;

    public LessonMiner(
        IReasoningTraceStore traceStore,
        IRatingStore ratingStore,
        ILessonStore lessonStore,
        IGeneralChatClient chatClient,
        IOptions<SelfImprovementOptions> options,
        ILogger<LessonMiner> logger,
        SemanticMemoryService? memory = null)
    {
        _traceStore = traceStore;
        _ratingStore = ratingStore;
        _lessonStore = lessonStore;
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
        _memory = memory;
    }

    /// <summary>Aday trace'leri tarar, LLM'e analiz ettirir, üretilen lesson'ları store'a ekler.</summary>
    public async Task<MiningRunReport> MineAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return new MiningRunReport { Skipped = true, SkipReason = "self-improvement disabled" };

        var traces = _traceStore.GetRecent(_options.RecentTracesToScan);
        var lowRatings = _ratingStore.GetAll()
            .Where(r => r.Stars <= _options.MinRatingForLesson)
            .ToDictionary(r => r.SessionId, r => r);

        var candidates = new List<ReasoningTrace>();
        foreach (var t in traces)
        {
            if (lowRatings.ContainsKey(t.SessionId)) { candidates.Add(t); continue; }
            if (!string.IsNullOrEmpty(t.Error)) { candidates.Add(t); continue; }
            if (t.TerminationReason is "timeout" or "error") { candidates.Add(t); continue; }
            if (t.Reasoning?.SanityIssues?.Any(i => i.Severity == IssueSeverity.Error) == true)
                candidates.Add(t);
        }

        if (candidates.Count == 0)
        {
            _logger.LogInformation("LessonMiner: aday trace yok.");
            return new MiningRunReport { Candidates = 0, ProposedLessons = 0 };
        }

        // En yeni 8'i ile sınırla — token bütçesi
        var picked = candidates.OrderByDescending(t => t.StartedAt).Take(8).ToList();
        var prompt = BuildAnalysisPrompt(picked, lowRatings);

        string llmText;
        try
        {
            llmText = await _chatClient.CompleteAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LessonMiner: LLM çağrısı başarısız.");
            return new MiningRunReport { Candidates = picked.Count, Error = ex.Message };
        }

        var parsed = TryParseLessons(llmText);
        var added = new List<Lesson>();
        foreach (var l in parsed)
        {
            l.SourceTraceIds.AddRange(picked.Select(t => t.TraceId));
            _lessonStore.Add(l);
            added.Add(l);
        }

        _logger.LogInformation("LessonMiner: {Cand} aday → {Added} lesson önerisi üretildi.",
            picked.Count, added.Count);

        return new MiningRunReport
        {
            Candidates = picked.Count,
            ProposedLessons = added.Count,
            LessonIds = added.Select(l => l.Id).ToList()
        };
    }

    /// <summary>Lesson'ı approve eder, opsiyonel olarak VectorStore Lessons collection'ına yazar.</summary>
    public async Task<bool> ApproveAsync(string lessonId, string decidedBy, string? reason, CancellationToken ct = default)
    {
        var lesson = _lessonStore.Get(lessonId);
        if (lesson is null || lesson.Status != LessonStatus.Proposed) return false;

        lesson.Status = LessonStatus.Approved;
        lesson.DecidedBy = decidedBy;
        lesson.DecidedAt = DateTime.UtcNow;
        lesson.DecisionReason = reason;

        // VectorStore Lessons collection'a yaz — sonraki konuşmalar context olarak alır
        if (_memory is { Enabled: true })
        {
            try
            {
                var doc = new MemoryDocument
                {
                    Id = lesson.Id,
                    Kind = MemoryKind.Lesson,
                    Title = lesson.Title,
                    Source = $"lesson:{lesson.Id}",
                    Text = $"{lesson.Title}\n\n{lesson.LessonText}\n\n[Gözlem: {lesson.Observation}]",
                    Tags = { ["agent"] = lesson.SuggestedAgent ?? "any" }
                };
                await _memory.UpsertAsync(doc, ct);
                lesson.VectorMemoryId = doc.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lesson VectorStore'a yazılamadı (lessonId={Id}); status Approved ama yalnızca DB'de.", lessonId);
            }
        }
        _lessonStore.Update(lesson);
        return true;
    }

    public bool Reject(string lessonId, string decidedBy, string? reason)
    {
        var lesson = _lessonStore.Get(lessonId);
        if (lesson is null || lesson.Status != LessonStatus.Proposed) return false;

        lesson.Status = LessonStatus.Rejected;
        lesson.DecidedBy = decidedBy;
        lesson.DecidedAt = DateTime.UtcNow;
        lesson.DecisionReason = reason;
        _lessonStore.Update(lesson);
        return true;
    }

    // ─── helpers ───

    private static List<ConversationMessage> BuildAnalysisPrompt(
        List<ReasoningTrace> traces, IDictionary<string, ConversationRating> lowRatings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Aşağıda müşteri destek bot'unun başarısız veya düşük puanlı konuşma trace'leri var.");
        sb.AppendLine("Her trace'i analiz et ve sistemin gelecekte daha iyi olabilmesi için somut, uygulanabilir DERS'ler çıkar.");
        sb.AppendLine();
        sb.AppendLine("Yanıtın SADECE şu şemada JSON olmalı:");
        sb.AppendLine("""{ "lessons": [ { "title": "...", "lesson": "X durumunda Y yap", "observation": "...", "suggestedAgent": "ProductAgent|null" } ] }""");
        sb.AppendLine();
        sb.AppendLine("--- TRACE'LER ---");

        foreach (var t in traces)
        {
            sb.AppendLine();
            sb.AppendLine($"### TRACE {t.TraceId} (session={t.SessionId})");
            sb.AppendLine($"- Soru: {Trim(t.UserQuery, 240)}");
            sb.AppendLine($"- Yanıt: {Trim(t.FinalResponse ?? "(yok)", 240)}");
            sb.AppendLine($"- Sonlanma: {t.TerminationReason ?? "?"} | Hata: {t.Error ?? "yok"}");
            sb.AppendLine($"- Iterasyon: {t.IterationCount} | Süre(ms): {t.DurationMs ?? -1}");
            if (lowRatings.TryGetValue(t.SessionId, out var rating))
                sb.AppendLine($"- KullanıcıPuanı: {rating.Stars}★ — \"{Trim(rating.Feedback ?? "", 200)}\"");
            if (t.Reasoning?.SanityIssues?.Count > 0)
                sb.AppendLine($"- SanityIssues: {string.Join("; ", t.Reasoning.SanityIssues.Select(i => $"[{i.Severity}] {i.Message}"))}");
            if (t.AgentVisits.Count > 0)
                sb.AppendLine($"- Agents: {string.Join(" → ", t.AgentVisits.Select(v => v.AgentName))}");
        }

        return new List<ConversationMessage>
        {
            new(ConversationRoles.System, "You are an expert in agentic system root-cause analysis. Output strict JSON."),
            new(ConversationRoles.User, sb.ToString())
        };
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private List<Lesson> TryParseLessons(string text)
    {
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json)) return new();

        try
        {
            var doc = JsonSerializer.Deserialize<LessonsEnvelope>(json, JsonOpts);
            if (doc?.Lessons is null) return new();

            return doc.Lessons.Select(l => new Lesson
            {
                Title = l.Title ?? "(başlıksız)",
                LessonText = l.Lesson ?? "",
                Observation = l.Observation ?? "",
                SuggestedAgent = string.IsNullOrWhiteSpace(l.SuggestedAgent) ||
                                  l.SuggestedAgent.Equals("null", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : l.SuggestedAgent
            })
            .Where(l => !string.IsNullOrWhiteSpace(l.LessonText))
            .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LessonMiner: LLM JSON parse edilemedi: {Snippet}",
                text.Length > 300 ? text[..300] : text);
            return new();
        }
    }

    private static string? ExtractJson(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return s.Substring(start, end - start + 1);
    }

    private sealed class LessonsEnvelope
    {
        public List<LessonRecord>? Lessons { get; set; }
    }
    private sealed class LessonRecord
    {
        public string? Title { get; set; }
        public string? Lesson { get; set; }
        public string? Observation { get; set; }
        public string? SuggestedAgent { get; set; }
    }
}

public sealed class MiningRunReport
{
    public bool Skipped { get; set; }
    public string? SkipReason { get; set; }
    public int Candidates { get; set; }
    public int ProposedLessons { get; set; }
    public List<string> LessonIds { get; set; } = new();
    public string? Error { get; set; }
}
