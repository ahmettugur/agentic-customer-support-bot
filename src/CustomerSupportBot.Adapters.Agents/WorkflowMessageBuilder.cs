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

/// <summary>
/// Workflow'a gidecek prompt: mesajlar + bağlamın <b>nasıl kurulduğu</b>.
///
/// <para>
/// <see cref="Context"/> yalnızca gözlemlenebilirlik için değil: çağıran, özetin bu turda
/// gerçekten prompt'a girip girmediğine göre geçmişi kırpma kararı veriyor
/// (bkz. <see cref="WorkflowMessageBuilder.SelectHistoryToSend"/>).
/// </para>
/// </summary>
internal sealed record WorkflowPrompt(List<ChatMessage> Messages, ContextResult Context);

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
    /// Burada üretilen sistem mesajlarının (bağlam, reasoning özeti, replan notu) içeriği ve
    /// sırası, `CustomerSupportBot.Api/Prompts/agents/*.md` altındaki specialist prompt'larıyla
    /// BELGESİZ (kod dışında yazılı olmayan) bir sözleşme oluşturur. Bu metni veya mesaj sırasını
    /// değiştirirken ilgili prompt dosyalarının da gözden geçirilmesi gerekir; derleyici/test bu
    /// bağlantıyı doğrulamaz.
    ///
    /// <para>
    /// Not: Eskiden burada ayrıca deterministik bir "entity hint" (order_id/customer_id/
    /// complaint_id'nin metinden regex ile çıkarılıp "order_id MEVCUT: 1041" gibi bir sistem
    /// mesajı olarak enjekte edilmesi, <c>IdExtractor</c> üzerinden) vardı. Bu mekanizma
    /// kaldırıldı — order_id/complaint_id çözümü artık tamamen LLM'e bırakılıyor: specialist
    /// agent'lar kullanıcı mesajını doğrudan okuyup ilgili tool'a parametre olarak geçiriyor;
    /// hiç geçmezse <c>get_last_order_tool</c> gibi parametresiz tool'lar devreye giriyor
    /// (bkz. <c>planning-agent.md</c>). Prompt dosyaları "ENTITY EXTRACTION'dan veya mesajdan"
    /// diye ikili bir kaynak tarif eder — ilk kaynak artık hiç dolmaz, ikincisi (mesajdan)
    /// değişmeden çalışmaya devam eder.
    /// </para>
    /// </summary>
    public async Task<WorkflowPrompt> BuildWorkflowMessagesAsync(
        string query,
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ReasoningResult? reasoning,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();

        var identityHint = await _identityHint.BuildAsync(session, ct);
        if (!string.IsNullOrWhiteSpace(identityHint))
        {
            messages.Add(new ChatMessage(ChatRole.System, identityHint));
        }

        ContextResult contextResult = ContextResult.Empty;
        if (session != null)
        {
            contextResult = await _contextPipeline.BuildContextAsync(session, query, ct);
            if (!string.IsNullOrWhiteSpace(contextResult.Text))
            {
                messages.Add(new ChatMessage(ChatRole.System,
                    $"Aşağıdaki bağlam bilgileri mevcut oturum hakkındadır. " +
                    $"Bu bilgileri yanıtlarınızda dikkate alın:\n\n{contextResult.Text}"));
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

        foreach (var m in SelectHistoryToSend(conversationHistory, session, contextResult))
            messages.Add(new ChatMessage(ToChatRole(m.Role), m.Text));

        var replanHint = ConsumeForceReplanHint(session);
        if (replanHint != null)
        {
            messages.Add(new ChatMessage(ChatRole.System, replanHint));
        }

        messages.Add(new ChatMessage(ChatRole.User, query));

        return new WorkflowPrompt(messages, contextResult);
    }

    /// <summary>
    /// Geçmişin prompt'a gidecek kısmını seçer: özetlenmiş mesajlar atlanır, kalanlar birebir.
    ///
    /// <para>
    /// <b>Düzeltilen hata:</b> Eskiden geçmiş koşulsuz olarak baştan sona ekleniyordu. Oysa
    /// <c>ConversationSummaryProvider</c> 8. mesajdan itibaren eski turları özetleyip bağlama
    /// koyuyor — yani aynı turlar hem özet hem ham hâliyle gönderiliyordu. Özetleme, maliyeti
    /// azaltmak yerine <b>artırıyordu</b>: tam geçmiş + özet + özeti üretmek için fazladan bir
    /// LLM çağrısı. Artık özet, özetlediği aralığın yerine geçiyor.
    /// </para>
    ///
    /// <para>
    /// Sınır <c>SessionState.SummarizedMessageCount</c>'tan okunur. Savunmacı biçimde kırpılır:
    /// sayı geçmişten büyükse (ör. geçmiş temizlenmiş ama state kalmışsa) hiçbir şey atlanmaz —
    /// özetlenmemiş bir mesajı düşürmektense fazladan mesaj göndermek yeğdir.
    /// </para>
    ///
    /// <para>
    /// <b>Kritik koşul:</b> atlama, özetin <b>bu turda gerçekten prompt'a girmiş olmasına</b>
    /// bağlıdır (<c>contextResult</c>). Yalnızca oturum durumuna bakmak bir regresyon
    /// üretiyordu: özetleyici LLM çağrısı hata verdiğinde/zaman aşımına uğradığında provider
    /// <c>null</c> döner ama <c>SessionState.ConversationSummary</c> eski değerini korur —
    /// böylece özet prompt'ta olmaz, geçmiş yine de atlanır ve o turlar modelin görüş
    /// alanından tamamen kaybolurdu.
    /// </para>
    /// </summary>
    internal static IEnumerable<ConversationMessage> SelectHistoryToSend(
        List<ConversationMessage>? conversationHistory,
        AgentSession? session,
        ContextResult contextResult)
    {
        if (conversationHistory is not { Count: > 0 })
            return [];

        var summarized = session?.State.SummarizedMessageCount ?? 0;

        // Özet yoksa atlama da yok — sayı state'te kalmış olabilir, ona güvenme.
        if (summarized <= 0 || string.IsNullOrWhiteSpace(session?.State.ConversationSummary))
            return conversationHistory;

        // Özet bu tur prompt'a girmediyse (hata/timeout/bütçe) geçmişi kırpma.
        if (!contextResult.Included(ConversationSummaryProvider.ProviderName))
            return conversationHistory;

        if (summarized >= conversationHistory.Count)
            return conversationHistory;

        return conversationHistory.Skip(summarized);
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
