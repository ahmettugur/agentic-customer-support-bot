// Adapters.Agents/TurnFinalizer.cs
// Bir workflow turu tamamlandığında (başarı, timeout veya hata) tetiklenmesi gereken
// yan etkileri toplar: eskalasyon işleme, ajan ziyareti çıktılarını doldurma,
// episodik bellek yazımı, müşteri profili güncellemesi ve trace kapatma.

using System.Text.Json;
using System.Text.Json.Serialization;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class TurnFinalizer
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IReasoningTraceStore _traceStore;
    private readonly ApprovalGateService _approvalGate;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISemanticMemoryWriter? _semanticMemory;
    private readonly ICustomerProfileService? _profileService;

    public TurnFinalizer(
        IReasoningTraceStore traceStore,
        ApprovalGateService approvalGate,
        ILoggerFactory loggerFactory,
        ISemanticMemoryWriter? semanticMemory,
        ICustomerProfileService? profileService)
    {
        _traceStore = traceStore;
        _approvalGate = approvalGate;
        _loggerFactory = loggerFactory;
        _semanticMemory = semanticMemory;
        _profileService = profileService;
    }

    /// <summary>
    /// Workflow başarıyla tamamlandığında trace'i kapatır ve bağlı yan etkileri
    /// (eskalasyon işleme, episodik bellek, müşteri profili) tetikler.
    /// </summary>
    /// <summary>
    /// Bir workflow koşusunun sonunu kapatır.
    ///
    /// <para>
    /// <paramref name="isSubTaskRun"/> — bileşik (compound) bir sorgunun ALT görev koşusu.
    /// Böyle koşularda <b>tur bazlı yan etkiler</b> (episodic bellek kaydı, müşteri profili
    /// etkileşim sayacı) atlanır; onlar birleştirilmiş tur için bir kez, aggregate sonuçla
    /// yazılır (bkz. <see cref="FinalizeAggregateTurnAsync"/>).
    /// </para>
    ///
    /// <para>
    /// Bu ayrım olmadan tek bir kullanıcı mesajı N alt göreve bölündüğünde profil sayacı N tur
    /// ilerliyor ve birbirinden kopuk N sentetik episode yazılıyordu. Episodic bellek sonradan
    /// aranan bir kaynak olduğu için bu, kalıcı olarak kirlenmiş bir bellek demekti.
    /// </para>
    ///
    /// <para>
    /// Eskalasyon ve trace tamamlama alt koşularda da çalışır: her alt koşunun kendi trace'i
    /// vardır ve kapatılmalıdır, alt görevde doğan bir eskalasyon da kaybolmamalıdır.
    /// </para>
    /// </summary>
    public async Task FinalizeAsync(
        ReasoningTrace trace,
        AgentSession? session,
        string query,
        string result,
        string terminationReason,
        CancellationToken ct = default,
        bool isSubTaskRun = false)
    {
        await _approvalGate.ProcessPendingEscalationsAsync(trace, query, result, ct);
        PopulateAgentVisitOutputs(trace, result);

        if (!isSubTaskRun)
        {
            WriteEpisodicMemorySafe(trace, query, result, session?.State.AuthenticatedCustomerId);
            await UpdateCustomerProfileSafeAsync(session, trace, query, result, ct);
        }

        _traceStore.Complete(trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);
    }

    /// <summary>
    /// Bileşik bir turun tur bazlı yan etkilerini <b>bir kez</b> yazar: kullanıcının tek
    /// mesajı ve alt görev sonuçlarının birleşimi.
    ///
    /// <para>
    /// Trace'e dokunmaz — alt koşuların kendi trace'leri zaten kapatılmıştır. Buradaki tek iş,
    /// belleğin ve profilin turu <b>bir</b> etkileşim olarak görmesidir.
    /// </para>
    /// </summary>
    public async Task FinalizeAggregateTurnAsync(
        AgentSession? session,
        string query,
        string aggregateResult,
        string? intent,
        CancellationToken ct = default)
    {
        // Birleşik turun kendi trace'i YOKTUR — alt koşuların her biri kendi trace'ini zaten
        // kapatmıştır. Bu yüzden burada trace değil, belleğin ihtiyaç duyduğu iki alan
        // (oturum ve intent) doğrudan taşınır.
        var sessionId = session?.SessionId ?? "";
        WriteEpisodicMemory(sessionId, traceId: "", query, aggregateResult, intent,
            session?.State.AuthenticatedCustomerId);
        await UpdateCustomerProfileAsync(session, intent, query, aggregateResult, ct);
    }

    /// <summary>internal — bkz. WorkflowRunnerPureLogicTests deseni: saf mantık, doğrudan testlenebilir.</summary>
    internal static void PopulateAgentVisitOutputs(ReasoningTrace trace, string finalResult)
    {
        const int MaxLen = 1500;
        static string Truncate(string s) => s.Length <= MaxLen ? s : s[..MaxLen] + "…";

        foreach (var visit in trace.AgentVisits)
        {
            if (!string.IsNullOrWhiteSpace(visit.Output)) continue;

            var name = visit.AgentName ?? "";

            string? output = null;

            // Bulgu 4.5: eskiden `name.Split('_', 2)[0]` ile bir "temel ad" çıkarılıp bu
            // sabit "Planning"/"Response" string literalleriyle ve altta specialist eşleşmesinde
            // TERS yönde (s.AgentName.StartsWith(baseName, ...)) karşılaştırılıyordu. Codebase'in
            // kendi kurulu deseni (bkz. WorkflowResponseExtractor.ExtractSpecialistReasonings)
            // TERSİNE yönlü çalışır: (muhtemelen suffix'li) ham değerin TEMİZ
            // WellKnown.AgentNames sabitiyle mi BAŞLADIĞINA bakılır, temiz sabit asla split
            // edilmez. Aynı desene hizalandı — hem string literal (WellKnown.AgentNames.Planning/
            // .Response) hem karşılaştırma yönü. Not: bu, K3 analizinin işaret ettiği "gelecekte
            // ajan adı '_' içerirse kırılır" riskine karşı bir SAĞLAMLAŞTIRMA — bugün gerçek
            // ajan adı kümesinde ('_' içermeyen WellKnown.AgentNames sabitleri) davranışı
            // değiştirmez, mutasyon testinde de bu yüzden gözlemlenebilir bir fark çıkmadı
            // (kurulabilecek her makul senaryoda eski/yeni kod aynı sonucu üretiyor).
            if (name.StartsWith(WellKnown.AgentNames.Planning, StringComparison.OrdinalIgnoreCase) && trace.Planning != null)
            {
                output = JsonSerializer.Serialize(trace.Planning, PrettyJson);
            }
            else if (name.StartsWith(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase))
            {
                output = finalResult;
            }
            else if (trace.SpecialistReasonings.Count > 0)
            {
                // s.AgentName her zaman TEMİZ bir WellKnown.AgentNames değeridir (bkz.
                // ExtractSpecialistReasonings — SpecialistReasoningParser.TryParse'a çağıran
                // tarafından çözülmüş 'matchedName' geçirilir, ham executor id değil).
                var matching = trace.SpecialistReasonings
                    .Where(s => name.StartsWith(s.AgentName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matching.Count > 0)
                {
                    output = JsonSerializer.Serialize(matching.Count == 1 ? matching[0] : (object)matching, PrettyJson);
                }
            }

            if (!string.IsNullOrWhiteSpace(output))
                visit.Output = Truncate(output);
        }
    }

    private void WriteEpisodicMemorySafe(ReasoningTrace trace, string query, string response, string? customerId)
        => WriteEpisodicMemory(trace.SessionId, trace.TraceId, query, response,
            trace.Reasoning?.Intent, customerId);

    private void WriteEpisodicMemory(
        string sessionId, string traceId, string query, string response, string? intent, string? customerId)
    {
        if (_semanticMemory is null || !_semanticMemory.Enabled) return;
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(response)) return;

        var memory = _semanticMemory;
        var logger = _loggerFactory.CreateLogger<TurnFinalizer>();

        _ = Task.Run(async () =>
        {
            try
            {
                await memory.WriteEpisodeAsync(sessionId, traceId, query, response, intent, rating: null, customerId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Episodic memory yazımı başarısız oldu (traceId={TraceId})", traceId);
            }
        });
    }

    private Task UpdateCustomerProfileSafeAsync(
        AgentSession? session,
        ReasoningTrace trace,
        string query,
        string response,
        CancellationToken ct)
        => UpdateCustomerProfileAsync(session, trace.Reasoning?.Intent, query, response, ct);

    private async Task UpdateCustomerProfileAsync(
        AgentSession? session,
        string? intent,
        string query,
        string response,
        CancellationToken ct)
    {
        if (_profileService is null) return;
        // AuthenticatedCustomerId (JWT) — State.CustomerId DEĞİL: aksi halde kullanıcı
        // "ben 1008'im" diyerek bu konuşmayı BAŞKA bir müşterinin kalıcı profiline
        // yazdırabilir (profil özeti/ilgi alanları/dil tercihi kalıcı olarak bozulur).
        var customerId = session?.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return;

        var logger = _loggerFactory.CreateLogger<TurnFinalizer>();

        try
        {
            await _profileService.RecordInteractionAsync(
                customerId: customerId,
                userQuery: query,
                botResponse: response,
                intent: intent,
                rating: null,
                isNewSession: false,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Customer profile güncellemesi başarısız (customerId={Id})", customerId);
        }
    }
}
