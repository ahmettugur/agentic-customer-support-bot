// Adapters.Agents/WorkflowTraceEventProcessor.cs
// WorkflowRunner'dan ayrıştırıldı (#47): tek bir workflow koşusunun trace toplama durumu
// ve workflow event'lerinin (ExecutorInvoked/Completed, AgentResponseUpdate, WorkflowOutput)
// trace yan etkilerine + stream event'lerine çevrilmesi. Orkestrasyon (RunAsync/RunStreamingAsync)
// WorkflowRunner'da kalır; bu sınıf yalnızca "bir event geldiğinde trace'e ne olur" sorusuna cevap verir.

using System.Text;
using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using AgentSession = CustomerSupportBot.Domain.Model.AgentSession;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents;

internal sealed class WorkflowTraceEventProcessor
{
    private readonly IReasoningTraceStore _traceStore;
    private readonly IApprovalContextAccessor _approvalContext;

    public WorkflowTraceEventProcessor(IReasoningTraceStore traceStore, IApprovalContextAccessor approvalContext)
    {
        _traceStore = traceStore;
        _approvalContext = approvalContext;
    }

    /// <summary>
    /// Tek bir workflow koşusunun trace toplama durumu.
    /// </summary>
    internal sealed class TraceState
    {
        public required ReasoningTrace Trace { get; init; }
        public Dictionary<string, AgentVisit> ActiveVisits { get; } = new();
        public string? LastAgentSignature { get; set; }
        public int IterationCount { get; set; }
        public string Result { get; set; } = "";

        /// <summary>
        /// ResponseAgent'ın gerçek token akışından en az bir karakter yayınlandıysa true —
        /// bu turda <see cref="StreamEventTypes.ResponseStart"/> zaten gönderilmiş demektir,
        /// döngü sonrası kod tekrar göndermemeli ve SplitIntoDeltaChunks ile tamamlanmış-metin
        /// parçalamasına başvurmamalıdır.
        /// </summary>
        public bool ResponseStreamStarted { get; set; }
        public ResponseStreamFilter ResponseFilter { get; } = new();
    }

    /// <summary>
    /// ResponseAgent'ın ham token akışını kullanıcıya göndermeden önce "TERMINATE: reason=..."
    /// işaretinden (ve ondan sonra gelen self-critique JSON bloğundan, bkz. response-agent.md)
    /// arındırır. Yalnızca satır başındaki olası protokol öneki tamponlanır;
    /// normal metin geciktirilmeden aktarılır.
    /// </summary>
    internal sealed class ResponseStreamFilter
    {
        private readonly StringBuilder _pending = new();
        private bool _cutoff;
        private bool _atLineStart = true;
        private int _prefixLength;

        public string Feed(string chunk)
        {
            if (_cutoff || string.IsNullOrEmpty(chunk)) return "";

            var output = new StringBuilder();
            foreach (var ch in chunk)
            {
                // Buffer only a possible protocol prefix at the beginning of a line.
                if (_atLineStart && _prefixLength == 0 && ch is ' ' or '\t')
                {
                    _pending.Append(ch);
                    continue;
                }
                if (_atLineStart && ch == TerminationProtocol.Prefix[_prefixLength])
                {
                    _pending.Append(ch);
                    if (++_prefixLength == TerminationProtocol.Prefix.Length)
                    {
                        _pending.Clear();
                        _cutoff = true;
                        break;
                    }
                    continue;
                }
                output.Append(_pending).Append(ch);
                _pending.Clear();
                _prefixLength = 0;
                _atLineStart = ch == '\n';
            }
            return output.ToString();
        }

        /// <summary>
        /// Akış sonunda tamamlanmamış protokol adayını normal metin olarak döndürür.
        /// Geçerli marker görülmüşse tampon boştur; ikinci yayına yol açmaz.
        /// </summary>
        public string Flush()
        {
            if (_cutoff || _pending.Length == 0) return "";
            var remaining = _pending.ToString();
            _pending.Clear();
            _prefixLength = 0;
            if (remaining.Length > 0) _atLineStart = false;
            return remaining;
        }
    }

    public TraceState StartTraceState(AgentSession? session, string query, ReasoningResult? reasoning)
    {
        var trace = _traceStore.StartTrace(session?.SessionId ?? "anonymous", query);
        _approvalContext.SetTraceId(trace.TraceId);
        if (reasoning != null)
        {
            trace.Reasoning = reasoning;
            _traceStore.Update(trace);
        }
        return new TraceState { Trace = trace };
    }

    // Bulgu 3.6: eskiden `List<StreamEvent>` idi — paylaşılan (tek örnek, tüm çağrılar arası
    // ortak) mutable bir liste. ApplyTraceEvent'in dönüş tipi de List<StreamEvent> olduğu için
    // hiçbir şey bir çağıranın (bugün yok, ama derleyici bunu ENGELLEMİYORDU) döndürülen
    // NoEvents üzerinde .Add(...) çağırmasını durdurmuyordu — öyle bir çağrı olsaydı, bu
    // singleton'ı process ömrü boyunca (tüm turlar, tüm session'lar için) kalıcı olarak
    // bozardı. Array.Empty<StreamEvent>() + IReadOnlyList<StreamEvent> dönüş tipiyle bu
    // yapısal olarak imkânsız hale getirildi.
    private static readonly IReadOnlyList<StreamEvent> NoEvents = Array.Empty<StreamEvent>();

    /// <summary>
    /// Bir workflow event'inin trace yan etkilerini uygular ve varsa bu event'ten
    /// kaynaklanan (boş olabilir) stream event listesini döner. Çağıran taraf streaming
    /// değilse (RunAsync) dönüş değerini yok sayabilir.
    /// </summary>
    public IReadOnlyList<StreamEvent> ApplyTraceEvent(TraceState st, WorkflowEvent evt)
    {
        switch (evt)
        {
            case ExecutorInvokedEvent invoked:
            {
                var executorId = invoked.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(executorId)) return NoEvents;

                // GroupChatHost her turda seçilmeyen tüm ajanlara da geçmişlerini senkron
                // tutmak için mesaj yollar (BroadcastAsync) — bu da Invoked/Completed
                // event çiftini tetikler ama ajan gerçekte çalışmaz. Süreye bakarak ayırt
                // etmek güvenilir değil (gerçek ajanlar da bazen <1ms'de tamamlanabiliyor);
                // asıl ayırt edici framework'ün TEK gerçek-tur sinyali olan TurnToken'dır —
                // sadece seçilen konuşmacı TurnToken alır, broadcast hedefleri ise düz
                // ChatMessage listesi alır (bkz. GroupChatHost.TakeTurnAsync/BroadcastAsync).
                if (invoked.Data is not TurnToken) return NoEvents;

                st.IterationCount++;

                var sig = $"{executorId}:running";
                if (sig == st.LastAgentSignature) return NoEvents;
                st.LastAgentSignature = sig;

                // Visit'i sadece ActiveVisits'e kaydet; trace listesine CompletedEvent'te
                // ekleyeceğiz.
                st.ActiveVisits[executorId] = new AgentVisit
                {
                    AgentName = executorId,
                    StartedAt = DateTime.UtcNow
                };

                return [new StreamEvent(StreamEventTypes.Agent, new { name = executorId, status = "running" })];
            }

            case ExecutorCompletedEvent completed:
            {
                var completedId = completed.ExecutorId ?? "unknown";
                if (WorkflowResponseExtractor.IsInternalWorkflowExecutor(completedId)) return NoEvents;

                // ActiveVisits'te kaydı yoksa bu, Invoked aşamasında TurnToken taşımadığı
                // için zaten atlanmış bir broadcast/senkron tamamlanmasıdır — yok say.
                if (!st.ActiveVisits.TryGetValue(completedId, out var visit)) return NoEvents;

                var sig = $"{completedId}:done";
                if (sig == st.LastAgentSignature) return NoEvents;
                st.LastAgentSignature = sig;

                visit.CompletedAt = DateTime.UtcNow;
                st.ActiveVisits.Remove(completedId);
                st.Trace.AgentVisits.Add(visit);

                // Planning/specialist reasoning'i her gerçek turda ara-durumdan da çıkar
                // (WorkflowOutputEvent'i beklemeden) — böylece timeout/hata ile workflow
                // hiç tamamlanmasa bile o ana kadar toplanan reflection'lar (ör.
                // needs_escalation) trace'e işlenmiş olur ve eskalasyon tetiklenebilir.
                if (completed.Data is IEnumerable<ChatMessage> pendingMessages)
                {
                    var planning = WorkflowResponseExtractor.ExtractPlanning(pendingMessages);
                    if (planning != null) st.Trace.Planning = planning;

                    var reasonings = WorkflowResponseExtractor.ExtractSpecialistReasonings(pendingMessages);

                    if (completedId.StartsWith(WellKnown.AgentNames.HumanHandoff, StringComparison.OrdinalIgnoreCase))
                        EnsureHumanHandoffEscalation(pendingMessages, reasonings);

                    if (completedId.StartsWith(WellKnown.AgentNames.Order, StringComparison.OrdinalIgnoreCase))
                        EnsureSideEffectToolCompletion(pendingMessages, reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

                    if (completedId.StartsWith(WellKnown.AgentNames.Complaint, StringComparison.OrdinalIgnoreCase))
                        EnsureSideEffectToolCompletion(pendingMessages, reasonings, WellKnown.AgentNames.Complaint, ComplaintAgentSideEffectTools);

                    if (reasonings.Count > 0) MergeSpecialistReasonings(st.Trace, reasonings);
                }

                _traceStore.Update(st.Trace);

                return [new StreamEvent(StreamEventTypes.Agent, new { name = completedId, status = "done" })];
            }

            // AgentResponseUpdateEvent, WorkflowOutputEvent'ten türer — daha spesifik olduğu
            // için switch'te ondan ÖNCE kontrol edilmeli. TurnToken(emitEvents:true) her
            // ajan turunda gerçek zamanlı LLM token delta'larını bu event tipiyle yayınlar
            // (bkz. AIAgentHostExecutor.InvokeAgentAsync). Sadece ResponseAgent'ın delta'ları
            // kullanıcıya gösterilir — diğer ajanların (Planning/Order/Complaint/HumanHandoff)
            // ham çıktısı yapılandırılmış JSON reasoning'dir, kullanıcıya asla akıtılmamalı.
            case AgentResponseUpdateEvent updateEvt:
            {
                var executorId = updateEvt.ExecutorId ?? "";
                if (!executorId.StartsWith(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase))
                    return NoEvents;

                var chunk = updateEvt.Update.Text;
                if (string.IsNullOrEmpty(chunk)) return NoEvents;

                var safeText = st.ResponseFilter.Feed(chunk);
                if (safeText.Length == 0) return NoEvents;

                var results = new List<StreamEvent>();
                if (!st.ResponseStreamStarted)
                {
                    st.ResponseStreamStarted = true;
                    results.Add(new StreamEvent(StreamEventTypes.ResponseStart, new { terminationReason = (string?)null }));
                }
                results.Add(new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(safeText)));
                return results;
            }

            case WorkflowOutputEvent output:
            {
                st.Result = WorkflowResponseExtractor.ExtractResultFromOutput(output);
                var planning = WorkflowResponseExtractor.ExtractPlanningFromOutput(output);
                if (planning != null) st.Trace.Planning = planning;
                var specialistReasonings =
                    WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(output);

                // Son güvence: turun tamamı burada görünür durumda — ExecutorCompletedEvent
                // aşamasında bir sebeple (event kaçırma, sıralama) yakalanamamışsa bile
                // human_handoff_tool çağrısı burada da kontrol edilir.
                if (output.Data is IEnumerable<ChatMessage> allMessages)
                {
                    EnsureHumanHandoffEscalation(allMessages, specialistReasonings);
                    EnsureSideEffectToolCompletion(allMessages, specialistReasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);
                    EnsureSideEffectToolCompletion(allMessages, specialistReasonings, WellKnown.AgentNames.Complaint, ComplaintAgentSideEffectTools);
                }

                if (specialistReasonings.Count > 0)
                    MergeSpecialistReasonings(st.Trace, specialistReasonings);
                if (planning != null || specialistReasonings.Count > 0)
                    _traceStore.Update(st.Trace);
                return NoEvents;
            }

            default:
                return NoEvents;
        }
    }

    /// <summary>
    /// Ajan başına en güncel reasoning'i tutarak birleştirir — aynı ajan hem ara-durumda
    /// (ExecutorCompletedEvent) hem final WorkflowOutputEvent'te görülebildiği için dedup
    /// gerekir; aksi halde eskalasyon adayı listesi mükerrer kayıt üretir.
    /// </summary>
    private static void MergeSpecialistReasonings(ReasoningTrace trace, List<SpecialistReasoning> incoming)
    {
        foreach (var r in incoming)
        {
            var idx = trace.SpecialistReasonings.FindIndex(x =>
                string.Equals(x.AgentName, r.AgentName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) trace.SpecialistReasonings[idx] = r;
            else trace.SpecialistReasonings.Add(r);
        }
    }

    private const string HandoffFallbackSummary =
        "Kullanıcı insan temsilciyle görüşme talep etti (human_handoff_tool çağrıldı).";

    private const string HandoffFallbackReason =
        "human_handoff_tool çağrıldı ama LLM reflection'ı needs_escalation olarak " +
        "işaretlemedi; sistem garantisiyle düzeltildi.";

    /// <summary>
    /// Kullanıcı açıkça insan temsilci istediğinde (human_handoff_tool çağrıldığında)
    /// eskalasyonun oluşmasını KOD İLE garanti eder — LLM'in postToolReflection'da
    /// <c>status=needs_escalation</c> yazmayı unutmasına/yanlış yazmasına bağlı kalmaz.
    /// EscalationPolicyService.ProcessPendingEscalationsAsync SADECE bu status'e bakarak
    /// eskalasyon açtığı için, model reflection JSON'unu yanlış üretirse kullanıcı
    /// "temsilci bağlanacak" mesajı alır ama admin panelinde hiçbir kayıt oluşmazdı —
    /// sessiz bir başarısızlık noktasıydı.
    ///
    /// <para>
    /// Bilinçli takas: garanti TEK YÖNLÜ. Tool çağrısı da bir LLM kararı olduğu için, model
    /// handoff tool'unu gereksiz çağırıp reflection'da "aslında gerek yok" dese bile kayıt
    /// açılır. Müşteri desteğinde kaçırılan eskalasyonun maliyeti fazladan eskalasyondan
    /// yüksek olduğu ve admin panelinde dismiss yolu bulunduğu için bu yön tercih edildi.
    /// Panelde gürültü artarsa bakılacak ilk yer burasıdır.
    /// </para>
    ///
    /// <para>
    /// Bilinen kapsam dışı köşe: HumanHandoffAgent turu uçuş hâlindeyken workflow timeout'a
    /// takılırsa tool çağrısı hiç tamamlanmadığı için FunctionCallContent oluşmaz ve garanti
    /// devreye giremez.
    /// </para>
    /// </summary>
    internal static void EnsureHumanHandoffEscalation(
        IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings)
    {
        if (!WorkflowResponseExtractor.ContainsHumanHandoffToolCall(messages)) return;

        var existing = reasonings.FirstOrDefault(r =>
            string.Equals(r.AgentName, WellKnown.AgentNames.HumanHandoff, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new SpecialistReasoning { AgentName = WellKnown.AgentNames.HumanHandoff };
            reasonings.Add(existing);
        }

        var reflection = existing.PostToolReflection ??= new PostToolReflection();

        // LLM zaten doğru işaretlemiş — hiçbir alanına dokunma.
        if (reflection.StatusEnum == TaskCompletionStatus.NeedsEscalation) return;

        // Yerinde düzelt (remove+replace DEĞİL): PreToolCheck, ResultConfidence, ResultNotes
        // ve MissingContext gibi LLM'in ürettiği diğer alanlar korunur. MissingContext ayrıca
        // fonksiyonel — EscalationPolicyService bunu doğrudan EscalationRequest'e kopyalıyor,
        // dolayısıyla kaybı admin'in gördüğü kayıttan "hangi bilgi eksikti"yi silerdi.
        reflection.Status = WellKnown.TaskStatuses.NeedsEscalation;
        reflection.TaskComplete = false;
        if (string.IsNullOrWhiteSpace(reflection.Summary)) reflection.Summary = HandoffFallbackSummary;
        if (string.IsNullOrWhiteSpace(reflection.HandoffReason)) reflection.HandoffReason = HandoffFallbackReason;
    }

    /// <summary>
    /// Ajan başına, HITL onayından geçen yan-etkili tool'lar — bkz.
    /// <see cref="EnsureSideEffectToolCompletion"/>. Elle yazılmış set yerine
    /// <see cref="WellKnown.SideEffectToolsOf"/> ile tek kaynaktan
    /// (<see cref="WellKnown.SideEffectToolOwners"/>) türetilir; yeni bir yazma tool'u
    /// eklendiğinde burada değişiklik gerekmez.
    /// </summary>
    private static readonly IReadOnlySet<string> OrderAgentSideEffectTools =
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Order);

    private static readonly IReadOnlySet<string> ComplaintAgentSideEffectTools =
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Complaint);

    /// <summary>
    /// <see cref="EnsureHumanHandoffEscalation"/> ile AYNI "tek yönlü kod garantisi" deseni,
    /// ama TERS yönde: orada "tool çağrıldıysa eskale et" idi, burada "tool BAŞARIYLA
    /// tamamlandıysa görevi tamamlandı say". Bu yan-etkili tool'ların (order_placement_tool,
    /// order_cancel_tool, return_request_tool, complaint_registration_tool — hepsi
    /// <c>WellKnown.HighRiskTools</c>'ta ve HITL onayından geçiyor) sonuçlarındaki
    /// <see cref="ToolResult.Success"/> alanı deterministik bir sinyal; LLM'in reflection'ı
    /// başarılı bir işlemi yanlışlıkla needs_followup/failed olarak işaretlerse müşteri
    /// "işlem yapılamadı" gibi yanlış-negatif bir yanıt alabilir ya da gereksiz bir ek tur
    /// (replan) tetiklenebilirdi.
    ///
    /// <para>
    /// Bloklamayan HITL modelinde <see cref="ToolResult.Success"/>=true İKİ farklı gerçek anlamına
    /// gelebilir: iş gerçekten tamamlandı, VEYA sadece onay kuyruğuna eklendi
    /// (<see cref="ToolResult.PendingApproval"/>=true, bkz. ApprovalGateService.ExecuteWithApprovalGateAsync).
    /// Bu ikisi karıştırılırsa (eskiden olduğu gibi ikisi de "done" sayılırsa) müşteri henüz
    /// gerçekleşmemiş bir işlemi "oldu" sanır — bu yüzden garanti artık iki yöne ayrılır: gerçek
    /// başarı → <see cref="TaskCompletionStatus.Done"/>, onay bekliyor → <see cref="TaskCompletionStatus.PendingApproval"/>.
    /// </para>
    ///
    /// <para>
    /// Bilinçli asimetri: yalnızca BAŞARI (done veya pending) yönünde düzeltilir. Tool başarısız
    /// olduysa veya sonucu belirlenemiyorsa (ör. <see cref="FunctionResultContent.Result"/> ne
    /// <see cref="ToolResult"/> ne tanınan bir <see cref="JsonElement"/> şemasında) hiç dokunulmaz
    /// — ters yönde zorlamak (başarısızlığı "done" yapmak) müşteriye gerçekleşmemiş bir işlemi
    /// "oldu" demek gibi çok daha riskli, tersine dönmesi zor bir hata olurdu. human_handoff'taki
    /// "kaçırılan eskalasyon > fazladan eskalasyon" takasının buradaki karşılığı: "kaçırılan başarı
    /// bildirimi (fazladan tur) &lt; yanlış başarı bildirimi (gerçekleşmemiş bir işlemi müşteriye
    /// onaylamak)".
    /// </para>
    ///
    /// <para>
    /// <paramref name="toolNames"/> içindeki BİRDEN FAZLA tool'dan biri başarılı olsa bile tek bir
    /// reflection kaydı üzerinde çalışılır (agentName başına bir <see cref="SpecialistReasoning"/>)
    /// — bir turda aynı ajanın birden fazla side-effect tool'u art arda çağırması beklenmez,
    /// ama çağırsa bile "en az biri pending ise genel sonuç pending" (daha zayıf garanti, yanlışlıkla
    /// "done" demektense daha güvenli).
    /// </para>
    /// </summary>
    internal static void EnsureSideEffectToolCompletion(
        IEnumerable<ChatMessage> messages,
        List<SpecialistReasoning> reasonings,
        string agentName,
        IReadOnlySet<string> toolNames)
    {
        var contents = messages.SelectMany(m => m.Contents).ToList();

        var relevantCallIds = contents.OfType<FunctionCallContent>()
            .Where(fc => toolNames.Contains(fc.Name))
            .Select(fc => fc.CallId)
            .ToHashSet(StringComparer.Ordinal);
        if (relevantCallIds.Count == 0) return;

        var outcomes = contents.OfType<FunctionResultContent>()
            .Where(fr => relevantCallIds.Contains(fr.CallId))
            .Select(fr => TryGetToolOutcome(fr.Result))
            .Where(o => o.Success == true)
            .ToList();
        if (outcomes.Count == 0) return;

        var pending = outcomes.Any(o => o.PendingApproval);
        var expectedStatus = pending ? TaskCompletionStatus.PendingApproval : TaskCompletionStatus.Done;
        var expectedTaskComplete = !pending;

        var existing = reasonings.FirstOrDefault(r =>
            string.Equals(r.AgentName, agentName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            existing = new SpecialistReasoning { AgentName = agentName };
            reasonings.Add(existing);
        }

        var reflection = existing.PostToolReflection ??= new PostToolReflection();

        // LLM zaten doğru işaretlemiş — hiçbir alanına dokunma.
        if (reflection.StatusEnum == expectedStatus && reflection.TaskComplete == expectedTaskComplete) return;

        reflection.Status = pending ? WellKnown.TaskStatuses.PendingApproval : WellKnown.TaskStatuses.Done;
        reflection.TaskComplete = expectedTaskComplete;
        if (string.IsNullOrWhiteSpace(reflection.Summary))
            reflection.Summary = pending
                ? "Talep onaya gönderildi; sonucu bildirim olarak iletilecek (sistem garantisiyle düzeltildi)."
                : "İşlem başarıyla tamamlandı (sistem garantisiyle status=done'a düzeltildi).";
    }

    /// <summary>
    /// AIFunctionFactory sonucu bazen ham <see cref="ToolResult"/> nesnesi, bazen (serileştirme
    /// yoluna bağlı olarak) <see cref="JsonElement"/> olarak taşır — ikisini de tek yerde
    /// normalize eder (bkz. mevcut test yardımcısı ApprovalGateServiceToolBuilderTests.ParseResult
    /// ile aynı desen).
    /// </summary>
    private static (bool? Success, bool PendingApproval) TryGetToolOutcome(object? raw)
    {
        if (raw is ToolResult tr) return (tr.Success, tr.PendingApproval);
        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            bool? success = je.TryGetProperty("success", out var s) &&
                (s.ValueKind == JsonValueKind.True || s.ValueKind == JsonValueKind.False)
                ? s.GetBoolean()
                : null;
            var pending = je.TryGetProperty("pendingApproval", out var p) && p.ValueKind == JsonValueKind.True;
            return (success, pending);
        }
        return (null, false);
    }

    /// <summary>
    /// Admin'e "bu tool neden çağrılıyor" sorusunun cevabı olarak gösterilecek gerekçeyi seçer.
    ///
    /// <para>
    /// <b>Neden preToolCheck.reasoning DEĞİL:</b> uzman ajanın <c>preToolCheck</c> alanı, final
    /// yapılandırılmış JSON çıktısının parçasıdır (aynı nesnede <c>postToolReflection</c> da var,
    /// yani tanımı gereği tool ÇALIŞTIKTAN sonra üretilir). Onay ise tool çalışmadan önceki ara
    /// turda tetiklenir — o anda böyle bir alan henüz mevcut değildir. Bu yüzden onay kaydına
    /// uzmanın kendi gerekçesi konulamaz.
    /// </para>
    ///
    /// <para>
    /// O anda gerçekten elde olan en bilgilendirici gerekçe PlanningAgent'ın routing
    /// rationale'ıdır: planlama turu uzman turundan ÖNCE tamamlandığı için trace'e çoktan
    /// işlenmiştir. Yoksa ReasoningService'in analizine, o da yoksa jenerik şablona düşülür.
    /// </para>
    /// </summary>
    public static string ResolveApprovalJustification(TraceState st)
    {
        var planningRationale = st.Trace.Planning?.Rationale;
        if (!string.IsNullOrWhiteSpace(planningRationale))
            return planningRationale;

        var reasoningRationale = st.Trace.Reasoning?.Rationale;
        if (!string.IsNullOrWhiteSpace(reasoningRationale))
            return reasoningRationale;

        return string.Empty; // ApprovalGateService jenerik şablona düşer
    }
}
