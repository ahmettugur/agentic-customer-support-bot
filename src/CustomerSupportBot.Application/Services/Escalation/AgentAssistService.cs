// Application/Services/Escalation/AgentAssistService.cs
// Temsilci asistanı — devralınan sohbet için özet, bağlam ve düzenlenebilir yanıt taslağı.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Escalation;

/// <summary>
/// <see cref="IAgentAssistPort"/> uygulaması. LLM yalnızca özet, müşteri talebi ve yanıt taslağı
/// için çağrılır; duygu, profil, ilgili makaleler ve açık işler doğrudan veriden gelir ve LLM
/// başarısız olsa bile döner. Taslak hiçbir zaman otomatik gönderilmez.
///
/// <para>
/// <b>Güvenlik:</b> profil yalnızca oturumun JWT'ye bağlı kimliğiyle (<c>AuthenticatedCustomerId</c>)
/// seçilir — LLM'in metinden çıkardığı <c>State.CustomerId</c> değil; aksi hâlde temsilciye başka bir
/// müşterinin profili gösterilebilirdi. Müşteri metni ve makaleler LLM'e <c>retrieved_data</c>
/// bloklarında veri olarak gider; prompt bu blokların içindeki talimatların uygulanmamasını söyler.
/// </para>
/// </summary>
public sealed partial class AgentAssistService : IAgentAssistPort
{
    /// <summary><c>Prompts/services/agent-assist.md</c>.</summary>
    public const string PromptKey = "services/agent-assist";

    private const int HistoryMessages = 20;
    private const int ArticleCount = 3;
    private const int SnippetLength = 300;

    private readonly ISessionManager _sessions;
    private readonly ICustomerProfileStore _profiles;
    private readonly IMemoryPort _memory;
    private readonly IEscalationSink _escalations;
    private readonly IApprovalQueue _approvals;
    private readonly IGeneralChatClient _llm;
    private readonly IPromptRepository _prompts;
    private readonly IContextSanitizer _sanitizer;
    private readonly ILogger<AgentAssistService> _logger;

    public AgentAssistService(
        ISessionManager sessions,
        ICustomerProfileStore profiles,
        IMemoryPort memory,
        IEscalationSink escalations,
        IApprovalQueue approvals,
        IGeneralChatClient llm,
        IPromptRepository prompts,
        IContextSanitizer sanitizer,
        ILogger<AgentAssistService> logger)
    {
        _sessions = sessions;
        _profiles = profiles;
        _memory = memory;
        _escalations = escalations;
        _approvals = approvals;
        _llm = llm;
        _prompts = prompts;
        _sanitizer = sanitizer;
        _logger = logger;
    }

    public async Task<AgentAssistResult?> GetAssistAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessions.GetAsync(sessionId, ct);
        if (session is null) return null;

        var history = (await _sessions.GetHistoryAsync(sessionId, ct)).TakeLast(HistoryMessages).ToList();
        var state = session.State;

        var sentiment = new AgentAssistSentiment(state.Sentiment, state.SentimentScore, state.ConsecutiveNegativeTurns);
        var profile = BuildProfile(state.AuthenticatedCustomerId);
        var articles = await SearchArticlesAsync(history, ct);
        var openItems = CollectOpenItems(sessionId);

        string? summary = null, request = null, reply = null, error = null;
        if (history.Count == 0)
        {
            error = "Henüz konuşma geçmişi yok.";
        }
        else
        {
            try
            {
                var raw = await _llm.CompleteAsync(BuildMessages(history, profile, articles, openItems), ct);
                (summary, request, reply) = ParseModelOutput(raw);
                if (summary is null && reply is null)
                    error = "Asistan yanıtı okunamadı; tekrar deneyin.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "AgentAssist: LLM çağrısı başarısız session={Sid}", sessionId);
                error = "Asistan şu anda özet ve taslak üretemiyor; tekrar deneyin.";
            }
        }

        return new AgentAssistResult(
            sessionId, summary, request, reply, error, sentiment, profile, articles, openItems);
    }

    private AgentAssistProfile? BuildProfile(string? authenticatedCustomerId)
    {
        if (string.IsNullOrWhiteSpace(authenticatedCustomerId)) return null;
        var p = _profiles.Get(authenticatedCustomerId);
        if (p is null) return null;

        return new AgentAssistProfile(
            p.CustomerId,
            p.Summary,
            p.PreferredTone,
            p.ProductInterests.Take(5).ToList(),
            p.IntentFrequency.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList(),
            p.RecentRatings.Count > 0 ? Math.Round(p.RecentRatings.Average(), 1) : null,
            p.TotalSessions);
    }

    private async Task<IReadOnlyList<AgentAssistArticle>> SearchArticlesAsync(
        IReadOnlyList<ConversationMessage> history, CancellationToken ct)
    {
        if (!_memory.Enabled) return [];

        var query = string.Join(" ", history
            .Where(m => m.Role == ConversationRoles.User)
            .TakeLast(2)
            .Select(m => m.Text));
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            var hits = await _memory.SearchAsync(MemoryKind.Knowledge, query, ArticleCount, ct);
            return hits.Select(h => new AgentAssistArticle(
                    h.Document.Title ?? h.Document.Source ?? "Bilgi tabanı",
                    Truncate(h.Document.Text, SnippetLength),
                    h.Document.Source,
                    Math.Round(h.Score, 3)))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AgentAssist: bilgi tabanı araması başarısız");
            return [];
        }
    }

    private List<AgentAssistOpenItem> CollectOpenItems(string sessionId)
    {
        var items = _escalations.GetOpen()
            .Where(e => e.SessionId == sessionId)
            .Select(e => new AgentAssistOpenItem("escalation", e.Id, e.Reason, e.Status.ToString(), e.CreatedAt))
            .ToList();

        items.AddRange(_approvals.GetPending()
            .Where(a => a.SessionId == sessionId)
            .Select(a => new AgentAssistOpenItem("approval", a.Id, a.ToolName, a.Status.ToString(), a.RequestedAt)));

        return items.OrderBy(i => i.CreatedAt).ToList();
    }

    private List<ConversationMessage> BuildMessages(
        IReadOnlyList<ConversationMessage> history,
        AgentAssistProfile? profile,
        IReadOnlyList<AgentAssistArticle> articles,
        IReadOnlyList<AgentAssistOpenItem> openItems)
    {
        var transcript = new StringBuilder();
        foreach (var m in history)
            transcript.Append(RoleLabel(m.Role)).Append(": ").AppendLine(m.Text);

        var context = new StringBuilder();
        context.AppendLine("KONUŞMA:");
        context.AppendLine(_sanitizer.WrapRetrieved(transcript.ToString(), "conversation"));

        if (profile is not null)
        {
            var line = $"Özet: {profile.Summary ?? "-"}; ton: {profile.PreferredTone}; " +
                       $"ilgi alanları: {string.Join(", ", profile.ProductInterests)}; " +
                       $"ortalama puan: {profile.AverageRating?.ToString("0.0") ?? "-"}";
            context.AppendLine("MÜŞTERİ PROFİLİ:");
            context.AppendLine(_sanitizer.WrapRetrieved(line, "customer_profile"));
        }

        if (openItems.Count > 0)
        {
            context.AppendLine("AÇIK İŞLER:");
            context.AppendLine(_sanitizer.WrapRetrieved(
                string.Join("\n", openItems.Select(i => $"- {i.Kind} ({i.Status}): {i.Description}")), "open_items"));
        }

        foreach (var a in articles)
        {
            context.AppendLine($"BİLGİ TABANI — {a.Title}:");
            context.AppendLine(_sanitizer.WrapRetrieved(a.Snippet, a.Source ?? "knowledge"));
        }

        return
        [
            new ConversationMessage(ConversationRoles.System, _prompts.Get(PromptKey)),
            new ConversationMessage(ConversationRoles.User, context.ToString())
        ];
    }

    private static string RoleLabel(string role) => role switch
    {
        ConversationRoles.User => "Müşteri",
        ConversationRoles.Assistant => "Bot",
        _ => role
    };

    /// <summary>Model çıktısı: <c>{summary, customerRequest, suggestedReply}</c> — kod bloğu içinde de olabilir.</summary>
    internal static (string? Summary, string? Request, string? Reply) ParseModelOutput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null, null);

        var match = JsonObjectRegex().Match(raw);
        if (!match.Success) return (null, null, null);

        try
        {
            using var doc = JsonDocument.Parse(match.Value);
            var root = doc.RootElement;
            return (Read(root, "summary"), Read(root, "customerRequest"), Read(root, "suggestedReply"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }

        static string? Read(JsonElement root, string name) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!.Trim()
                : null;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex JsonObjectRegex();
}
