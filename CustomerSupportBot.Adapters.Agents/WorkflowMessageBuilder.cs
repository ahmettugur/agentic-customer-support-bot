// Adapters.Agents/WorkflowMessageBuilder.cs
// WorkflowRunner'dan ayrıştırıldı (#47): workflow'a giden system/user mesajlarının inşası
// (bağlam, reasoning özeti, entity hint, replan notu, sipariş yönlendirme mesajının
// yeniden yazımı). Orkestrasyon (event loop) WorkflowRunner'da kalır.

using System.Text;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class WorkflowMessageBuilder
{
    private readonly IContextPipeline _contextPipeline;
    private readonly IPromptRepository _prompts;
    private readonly IChatClient _chatClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CustomerIdentityHintBuilder _identityHint;

    public WorkflowMessageBuilder(
        IContextPipeline contextPipeline,
        IPromptRepository prompts,
        IChatClient chatClient,
        ILoggerFactory loggerFactory,
        CustomerIdentityHintBuilder identityHint)
    {
        _contextPipeline = contextPipeline;
        _prompts = prompts;
        _chatClient = chatClient;
        _loggerFactory = loggerFactory;
        _identityHint = identityHint;
    }

    /// <summary>
    /// Burada üretilen sistem mesajlarının (bağlam, reasoning özeti, entity hint, replan notu)
    /// içeriği ve sırası, `CustomerSupportBot.Api/Prompts/agents/*.md` altındaki specialist
    /// prompt'larıyla BELGESİZ (kod dışında yazılı olmayan) bir sözleşme oluşturur — ör. entity
    /// hint'in metni (<see cref="IdExtractor.BuildHintMessage"/>) "order_id MEVCUT" gibi belirli
    /// ifadeler kullanır ve prompt'lar bu ifadeleri örnek/talimat olarak referans alır. Bu metni
    /// veya mesaj sırasını değiştirirken ilgili prompt dosyalarının da gözden geçirilmesi gerekir;
    /// derleyici/test bu bağlantıyı doğrulamaz.
    /// </summary>
    public async Task<List<ChatMessage>> BuildWorkflowMessagesAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning)
    {
        var messages = new List<ChatMessage>();

        var identityHint = await _identityHint.BuildAsync(session);
        if (!string.IsNullOrWhiteSpace(identityHint))
        {
            messages.Add(new ChatMessage(ChatRole.System, identityHint));
        }

        if (session != null)
        {
            var context = await _contextPipeline.BuildContextAsync(session, query);
            if (!string.IsNullOrWhiteSpace(context))
            {
                messages.Add(new ChatMessage(ChatRole.System,
                    $"Aşağıdaki bağlam bilgileri mevcut oturum hakkındadır. " +
                    $"Bu bilgileri yanıtlarınızda dikkate alın:\n\n{context}"));
            }
        }

        if (reasoning != null)
        {
            var hint = BuildReasoningSummaryHint(reasoning);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                messages.Add(new ChatMessage(ChatRole.System, hint));
            }
        }

        var extractedIds = ResolveExtractedIds(query, reasoning);
        var entityHint = IdExtractor.BuildHintMessage(extractedIds);
        if (!string.IsNullOrWhiteSpace(entityHint))
        {
            messages.Add(new ChatMessage(ChatRole.System, entityHint));
        }

        if (conversationHistory is { Count: > 0 })
            messages.AddRange(conversationHistory.Select(m => new ChatMessage(ToChatRole(m.Role), m.Text)));

        var replanHint = ConsumeForceReplanHint(session);
        if (replanHint != null)
        {
            messages.Add(new ChatMessage(ChatRole.System, replanHint));
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        return messages;
    }

    /// <summary>
    /// Workflow'a gidecek ENTITY EXTRACTION hint'i için ID kaynağını çözer.
    /// <c>reasoning.VerifiedEntities</c> mevcutsa (ReasoningService zaten <see cref="EntityVerifier"/>
    /// ile query+geçmiş+session+DB'yi birleştirip doğrulamış) o kullanılır — <c>IdExtractor.Extract(query)</c>
    /// yalnızca GÜNCEL mesaja bakar, önceki turdaki bağlamı (ör. "peki 1043" gibi bağlam kelimesiz
    /// bir takip mesajını) tamamen kaçırır. Bu yüzden bu iki yol tutarsız çalışıyordu: reasoning
    /// aşaması "1043"ü doğru bağlamda çözebilirken, workflow'un kendi (query-only) çıkarımı aynı
    /// sayıyı bağlamsız görüp "kısa mesaj → customer_id" varsayılanına düşüyor, specialist'e yanlış
    /// tool'u (get_last_order_tool yerine order_status_tool gerekirken) önerip yanlış-negatif
    /// "sipariş bulunamadı" yanıtı ürettiriyordu. <c>reasoning</c> null ise (ör. bazı çağıranlar
    /// reasoning'i atlıyor) eski (query-only) davranışa düşülür.
    /// </summary>
    internal static ExtractedIds ResolveExtractedIds(string query, ReasoningResult? reasoning)
    {
        var verified = reasoning?.VerifiedEntities;
        if (verified is null || !verified.HasAny)
            return IdExtractor.Extract(query);

        return new ExtractedIds
        {
            OrderId = verified.OrderId?.Value,
            CustomerId = verified.CustomerId?.Value,
            ComplaintId = verified.ComplaintId?.Value
        };
    }

    /// <summary>
    /// ForceReplanNextTurn bayrağını atomik olarak okur ve temizler — tek kullanımlık (one-shot)
    /// olması gerektiği için birden fazla eşzamanlı çağrının (paralel alt-görevler veya aynı
    /// session'a gelen eşzamanlı istekler) aynı flag'i birden çok kez tüketmesini engeller.
    /// </summary>
    private static string? ConsumeForceReplanHint(AgentSession? session)
    {
        if (session is null) return null;

        lock (session)
        {
            if (!session.State.ForceReplanNextTurn) return null;

            var hint = WellKnown.FallbackMessages.ReplanPlanningHint;
            if (!string.IsNullOrWhiteSpace(session.State.ReplanNote))
            {
                hint += $"\n\n📌 Admin notu (sadece sana, müşteri görmez): \"{session.State.ReplanNote}\"";
            }

            session.State.ForceReplanNextTurn = false;
            session.State.ReplanNote = null;
            return hint;
        }
    }

    internal string BuildReasoningSummaryHint(ReasoningResult r)
    {
        var linesBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(r.Analysis))
            linesBuilder.AppendLine($"- Analiz: {r.Analysis}");
        if (!string.IsNullOrWhiteSpace(r.Intent) && r.Intent != WellKnown.Intents.Unknown)
            linesBuilder.AppendLine($"- Niyet (nihai — ReasoningService kararı): {r.Intent}");
        if (r.Steps.Count > 0)
            linesBuilder.AppendLine($"- Önerilen adımlar: {string.Join(" → ", r.Steps.Select(s => s.Description))}");
        if (r.RequiredInfo.Count > 0)
            linesBuilder.AppendLine($"- Gerekli olduğu tahmin edilen bilgiler: {string.Join(", ", r.RequiredInfo)}");
        if (!string.IsNullOrWhiteSpace(r.NextAction))
            linesBuilder.AppendLine($"- Önerilen sonraki aksiyon: {r.NextAction}");

        // NOT: Burada eskiden SubTasks.Count >= 2 ise PlanningAgent'a "her birini sırayla
        // aynı yanıtta yönlendir" diyen bir COMPOUND QUERY bloğu vardı. Bu, DecomposedRunner'ın
        // gerçek yürütme yolunu YANLIŞ tarif ediyordu: SubTaskOrchestrator.IsCompoundQuery
        // (farklı hedef ajan sayısı >= 2 şartı) true olduğunda CreateSubTaskReasoning zaten
        // SubTasks'ı boşaltıp her alt görevi AYRI bir _runner.RunAsync çağrısıyla çalıştırıyor
        // — yani bu blok gerçek decompose senaryosunda hiç tetiklenmiyordu (subReasoning'de
        // SubTasks her zaman boş). Tek tetiklendiği durum IsCompoundQuery'nin false döndüğü
        // (aynı ajana hedeflenmiş >=2 alt görev) tek-runner yoluydu — orada da PlanningAgent'a
        // artık desteklenmeyen "sırayla yönlendir" formatını talep ediyordu (bkz. planning-agent.md:
        // PlanningAgent'ın tek görevi ilk ajanı seçmek, çıktı formatı JSON schema ile kısıtlı).
        var reasoningLines = linesBuilder.ToString().TrimEnd();
        return _prompts.Render("services/reasoning-hint", new Dictionary<string, string?>
        {
            ["REASONING_LINES"] = reasoningLines
        });
    }

    public async Task<string> RewriteRoutingMessageAsync(
        string routingMessage, string originalQuery, CancellationToken ct)
    {
        try
        {
            var prompt = new List<ChatMessage>
            {
                new(ChatRole.System, _prompts.Get("services/routing-rewrite-system")),
                new(ChatRole.User, _prompts.Render(
                    "services/routing-rewrite-user",
                    new Dictionary<string, string?>
                    {
                        ["ORIGINAL_QUERY"] = originalQuery,
                        ["ROUTING_MESSAGE"] = routingMessage
                    }))
            };

            var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: ct);
            return response.Text ?? WellKnown.FallbackMessages.RoutingRewrite;
        }
        catch (Exception ex)
        {
            // Sessizce yutmak, sürekli patlayan bir LLM çağrısını görünmez kılıyordu —
            // kullanıcı hep aynı fallback'i görür, sebebi hiçbir yere yazılmazdı.
            _loggerFactory.CreateLogger<WorkflowMessageBuilder>().LogWarning(
                ex, "Routing mesajı yeniden yazılamadı; fallback mesaj kullanılıyor.");
            return WellKnown.FallbackMessages.RoutingRewrite;
        }
    }

    private static ChatRole ToChatRole(string role) => role switch
    {
        ConversationRoles.User   => ChatRole.User,
        ConversationRoles.System => ChatRole.System,
        _                        => ChatRole.Assistant
    };
}
