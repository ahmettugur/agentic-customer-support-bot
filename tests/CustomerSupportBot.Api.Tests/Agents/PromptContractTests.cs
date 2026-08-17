// Tests/Agents/PromptContractTests.cs
//
// KOD ↔ PROMPT SÖZLEŞMESİ.
//
// WorkflowRunner.BuildWorkflowMessagesAsync, workflow'a system mesajları enjekte eder
// (entity hint, reasoning özeti, replan notu). Bu mesajların METNİ ile
// Prompts/agents/*.md dosyalarındaki talimatlar arasında yazılı olmayan bir sözleşme vardır:
// prompt'lar kodun ürettiği belirli ifadelere ("[ENTITY EXTRACTION]", tool adları,
// "Niyet (nihai — ReasoningService kararı)") ADIYLA atıf yapar.
//
// Bu bağlantıyı ne derleyici ne de başka bir test doğrular. Biri kod tarafındaki metni
// değiştirirse prompt'taki talimat sessizce boşa düşer — LLM artık var olmayan bir bloğa
// veya var olmayan bir tool'a yönlendirilir ve hata ancak canlıda, yanlış tool seçimi
// olarak görünür.
//
// Aşağıdaki testler sözleşmenin İKİ UCUNU birden kontrol eder: kodun ürettiği metin ile
// prompt dosyasının beklediği metin. Biri değişip diğeri değişmezse test kırmızıya döner.

using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Agents;

public class PromptContractTests
{
    private static readonly FileSystemPromptRepository Prompts =
        new(NullLogger<FileSystemPromptRepository>.Instance);

    private static string Prompt(string key) => Prompts.Get(key);

    /// <summary>Kodun gerçekten ürettiği entity hint metni (üç ID de dolu).</summary>
    private static string EntityHint() => IdExtractor.BuildHintMessage(new ExtractedIds
    {
        OrderId = "1030",
        CustomerId = "1027",
        ComplaintId = "1001"
    })!;

    /// <summary>
    /// BuildHintMessage'ın öncelik kuralı bir <b>if/else</b>: order_id doluysa yalnızca
    /// order_status_tool dalı, doluysa customer dalı hiç üretilmez. Tek bir örnekle test
    /// etmek dalların birini kör bırakır (bu ilk yazımda gerçekten oldu — mutasyon testinde
    /// get_last_order_tool yeniden adlandırması yakalanmadı). Bu yüzden tüm dallar toplanır.
    /// </summary>
    private static IEnumerable<string> AllHintVariants()
    {
        yield return IdExtractor.BuildHintMessage(new ExtractedIds { OrderId = "1030" })!;
        yield return IdExtractor.BuildHintMessage(new ExtractedIds { CustomerId = "1027" })!;
        yield return IdExtractor.BuildHintMessage(new ExtractedIds { ComplaintId = "1001" })!;
        yield return EntityHint();
    }

    private static List<string> ToolNamesIn(string text) =>
        Regex.Matches(text, @"\b[a-z_]+_tool\b").Select(m => m.Value).Distinct().ToList();

    private static List<string> AllReferencedToolNames() =>
        AllHintVariants().SelectMany(ToolNamesIn).Distinct().ToList();

    // ─── Halka 1: [ENTITY EXTRACTION] blok başlığı ────────────────────────────────

    [Fact]
    public void EntityHint_BlockMarker_IsReferencedByPlanningAgentPrompt()
    {
        // planning-agent.md: "`[ENTITY EXTRACTION]` system mesajında değerler varsa doğrudan kullan"
        // Kod bu başlığı üretmezse prompt'taki talimatın işaret ettiği blok hiç oluşmaz.
        const string marker = "[ENTITY EXTRACTION";

        EntityHint().Should().Contain(marker,
            "IdExtractor.BuildHintMessage bu blok başlığını üretmeli");
        Prompt("agents/planning-agent").Should().Contain(marker,
            "planning-agent.md bu blok başlığına adıyla atıf yapıyor");
    }

    // ─── Halka 2: Hint'in yönlendirdiği tool adları ───────────────────────────────

    [Fact]
    public void EntityHint_ReferencedToolNames_AreRealTools()
    {
        // BuildHintMessage tool adlarını string literal olarak gömüyor (WellKnown.ToolNames
        // kullanmıyor). Bir tool yeniden adlandırılırsa hint, LLM'i var olmayan bir tool'a
        // yönlendirir — bu test o sapmayı yakalar.
        var referenced = AllReferencedToolNames();

        referenced.Should().NotBeEmpty("hint bir tool önerisi içermeli");
        referenced.Should().HaveCountGreaterThan(1,
            "hem order_id hem customer_id dalı ayrı tool öneriyor — tek dal test edilirse diğeri kör kalır");

        var known = typeof(WellKnown.ToolNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        referenced.Should().OnlyContain(t => known.Contains(t),
            "hint'te geçen her tool adı WellKnown.ToolNames'te tanımlı olmalı");
    }

    [Fact]
    public void EntityHint_ReferencedToolNames_AreDocumentedInOrderAgentPrompt()
    {
        // Hint OrderAgent'ı belirli bir tool'a yönlendiriyor; o tool order-agent.md'de
        // tanımlı değilse ajan talimatı ile hint çelişir.
        var orderPrompt = Prompt("agents/order-agent");

        foreach (var tool in AllReferencedToolNames())
            orderPrompt.Should().Contain(tool,
                $"'{tool}' entity hint'inde öneriliyor, order-agent.md'de de tanımlı olmalı");
    }

    // ─── Halka 3: Reasoning hint'indeki "nihai niyet" satırı ──────────────────────

    [Fact]
    public void ReasoningHint_FinalIntentLabel_IsReferencedByPlanningAgentPrompt()
    {
        // planning-agent.md: "Reasoning hint'inde `Niyet (nihai — ReasoningService kararı): ...`
        // satırı varsa o intent nihai karardır". Etiket değişirse PlanningAgent intent'i
        // yeniden tahmin etmeye başlar — sessiz bir davranış regresyonu.
        const string label = "Niyet (nihai — ReasoningService kararı)";

        Prompt("agents/planning-agent").Should().Contain(label,
            "planning-agent.md bu etikete adıyla atıf yapıyor");

        // Kod tarafı: WorkflowMessageBuilder.BuildReasoningSummaryHint aynı etiketi üretir.
        // Metot instance olduğu ve tüm bağımlılıkları gerektirdiği için (Docker'lı fixture)
        // burada kaynak dosya üzerinden doğrulanır — amaç iki ucun senkronunu korumak.
        SourceOf("src/CustomerSupportBot.Adapters.Agents/WorkflowMessageBuilder.cs")
            .Should().Contain(label,
                "BuildReasoningSummaryHint bu etiketi üretmeli");
    }

    // ─── Halka 4: Replan notu ─────────────────────────────────────────────────────

    [Fact]
    public void ReplanHint_IsNonEmptyAndAddressesPlanningAgent()
    {
        // ConsumeForceReplanHint bu sabiti system mesajı olarak enjekte eder; PlanningAgent'ın
        // routing davranışını değiştirmesi beklenir. Boşalırsa admin "yeniden planla" butonu
        // sessizce etkisiz kalır.
        var hint = WellKnown.FallbackMessages.ReplanPlanningHint;

        hint.Should().NotBeNullOrWhiteSpace();
        hint.Should().Contain("ADMIN", "not admin kaynaklı olduğunu açıkça belirtmeli");
    }

    // ─── Halka 4b: pendingApproval alan adı ↔ ajan promptları ────────────────────
    // Bloklamayan HITL modelinde "onaya gönderildi" ile "iş tamamlandı" ayrımı ToolResult'ın
    // pendingApproval alanına dayanır. Kod tarafı (WorkflowTraceEventProcessor) bu alanı
    // camelCase JSON adıyla okur; ajan promptları da LLM'e AYNI alan adını gösterir.
    // Alan yeniden adlandırılıp promptlar güncellenmezse LLM olmayan bir alana bakar ve
    // bekleyen bir işlemi "tamamlandı" diye raporlar — sessiz, canlıda görülen bir hata.

    [Fact]
    public void PendingApprovalField_IsReferencedByEveryApprovalAwareAgentPrompt()
    {
        // Kod ucu: alan gerçekten var mı? (JSON'a camelCase serileşir.)
        typeof(ToolResult).GetProperty(nameof(ToolResult.PendingApproval))
            .Should().NotBeNull("ToolResult.PendingApproval, pending/done ayrımının tek kaynağıdır");

        const string jsonFieldName = "pendingApproval";

        // Kod ucu: trace işleyicisi bu adı okumalı.
        SourceOf("src/CustomerSupportBot.Adapters.Agents/WorkflowTraceEventProcessor.cs")
            .Should().Contain($"\"{jsonFieldName}\"",
                "EnsureSideEffectToolCompletion bu alanı JSON adıyla okur");

        // Prompt ucu: HITL onaylı tool'u olan ajanlar alanı adıyla anmalı.
        foreach (var key in new[] { "agents/order-agent", "agents/complaint-agent" })
            Prompt(key).Should().Contain(jsonFieldName,
                $"{key}.md, pending kararını bu alana göre vermeli (message metnine göre DEĞİL)");
    }

    [Fact]
    public void PendingApprovalStatus_IsSharedBetweenCodeAndPrompts()
    {
        // status="pending_approval" sabiti parser (NormalizeStatus) ile promptlar arasında
        // ortak sözleşmedir; biri değişip diğeri değişmezse status sessizce "done"a düşer
        // (NormalizeStatus tanımadığı değeri Done'a normalize eder).
        var status = WellKnown.TaskStatuses.PendingApproval;

        var json = "```json {\"postToolReflection\":{\"status\":\"" + status + "\"}} ```";
        SpecialistReasoningParser.TryParse(json, "OrderAgent")!
            .PostToolReflection!.StatusEnum
            .Should().Be(TaskCompletionStatus.PendingApproval,
                "parser bu status'u tanımalı, aksi halde sessizce done'a düşer");

        foreach (var key in new[] { "agents/order-agent", "agents/complaint-agent", "agents/response-agent" })
            Prompt(key).Should().Contain(status, $"{key}.md bu status değerini adıyla kullanmalı");
    }

    // ─── Halka 4c: selfCritique alanları ↔ parser ────────────────────────────────
    // response-agent.md her yanıtta bu bloğu ürettiriyor; SelfCritiqueParser okuyup trace'e
    // yazıyor ve LessonMiner inceleme adayı seçiminde kullanıyor. Prompt bir alan adını
    // değiştirirse parser onu sessizce varsayılana düşürür — kalite sinyali kaybolur ama
    // hiçbir şey patlamaz. Bu test o sessiz kaybı görünür kılar.

    [Fact]
    public void SelfCritiqueFields_InPromptAndParser_StayInSync()
    {
        var prompt = Prompt("agents/response-agent");

        foreach (var field in new[]
                 {
                     "addressesUserQuery", "tone", "completeness", "hallucinationRisk",
                     "sources", "issuesFound", "revisionNeeded", "revisionNotes"
                 })
        {
            prompt.Should().Contain(field, $"response-agent.md '{field}' alanını istemeli");
            SourceOf("src/CustomerSupportBot.Domain/Services/SelfCritiqueParser.cs")
                .Should().Contain($"\"{field}\"", $"SelfCritiqueParser '{field}' alanını okumalı");
        }

        // Ton değerleri de ortak sözleşme — IsConcerning bunlara göre karar veriyor.
        foreach (var tone in new[] { WellKnown.CritiqueTones.Robotic, WellKnown.CritiqueTones.Impolite })
            prompt.Should().Contain(tone, $"response-agent.md '{tone}' tonunu tanımlamalı");
    }

    [Fact]
    public void SelfCritique_IsActuallyConsumed_NotJustStripped()
    {
        // Bu blok uzun süre üretilip hiç okunmadı (yalnızca çıktıdan siliniyordu) ve prompt
        // "sistem tarafından okunur" diye yanlış beyanda bulunuyordu. Tüketim zincirinin
        // (parse → trace → LessonMiner) kopması hâlinde prompt yeniden yalan söylemeye başlar.
        SourceOf("src/CustomerSupportBot.Adapters.Agents/WorkflowRunner.cs")
            .Should().Contain("SelfCritiqueParser.TryParse", "ham çıktıdan parse edilmeli");
        SourceOf("src/CustomerSupportBot.Domain/Model/ReasoningTrace.cs")
            .Should().Contain("SelfCritique? SelfCritique", "trace'e yazılmalı");

        // Yorum satırında geçmesi YETMEZ — aday seçimini gerçekten sürüklemeli.
        SourceOf("src/CustomerSupportBot.Application/Services/Improvement/LessonMiner.cs")
            .Should().Contain("SelfCritique?.IsConcerning",
                "LessonMiner aday seçiminde bu sinyali kullanmalı; yalnızca yorumda anılması yeterli değil");
    }

    // ─── Halka 4d: PlanningAgent çıktı formatı ↔ strict JSON schema ──────────────
    // PlanningAgent ChatResponseFormat.ForJsonSchema<PlanningResult> ile yapılandırılmış:
    // çıktı ÇIPLAK bir JSON nesnesi olmak zorunda. Prompt uzun süre "iki bölüm" istiyordu
    // (fence'li JSON + ardından `1. OrderAgent : ...` düz metin routing satırları) — strict
    // schema altında üretilemez, fallback modda da hiçbir parser okumuyordu; model talimata
    // uymaya çalışırken routing metnini taskDescription gibi PARSE EDİLEN bir alana
    // sıkıştırabilirdi (o alan doğrudan specialist'e görev olarak geçiyor).

    [Fact]
    public void PlanningPrompt_DoesNotAskForTextOutsideTheJsonObject()
    {
        var prompt = Prompt("agents/planning-agent");

        prompt.Should().NotContain("Bölüm 2",
            "strict JSON schema altında JSON dışına metin yazılamaz");
        prompt.Should().NotContain("iki bölümden",
            "çıktı tek bir JSON nesnesidir");
        Regex.IsMatch(prompt, @"^\s*\d+\.\s*<?selectedAgent>?\s*:", RegexOptions.Multiline)
            .Should().BeFalse("routing satırı formatı hiçbir yerde parse edilmiyor");
    }

    [Fact]
    public void PlanningPrompt_DocumentsExactlyTheSchemaFields()
    {
        // Prompt'taki JSON örneği ile PlanningResult'ın alanları ayrışırsa, model şemanın
        // kabul etmediği bir alan üretmeye çalışır ya da gerçek bir alanı hiç doldurmaz.
        var prompt = Prompt("agents/planning-agent");

        var schemaFields = typeof(PlanningResult).GetProperties()
            .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..])
            .ToList();

        foreach (var field in schemaFields)
            prompt.Should().Contain($"\"{field}\"",
                $"planning-agent.md '{field}' alanını örnek çıktıda göstermeli (PlanningResult'ta var)");
    }

    // ─── Halka 4e: reasoning-system.md intent enum'u ↔ WellKnown.Intents ─────────
    // ReasoningResultParser intent'i HAM string olarak taşır (normalizasyon/alias katmanı
    // yok) ve ReasoningChatClient'ta JSON schema da yok — yani LLM'in üretebileceği intent
    // kümesini TEK BAŞINA prompt metni belirler. Tüketiciler (appsettings IntentSkillMap,
    // ParallelExecutionOptions.IsReadOnly) tam-string eşleşme yapar.
    //
    // Bu sapma gerçekten yaşandı: prompt "sipariş_iadesi" derken kod tarafı
    // WellKnown.Intents.ReturnRequest = "iade_talebi" tanımlıyordu. Sonuç: iade
    // eskalasyonları IntentSkillMap'teki "iade_talebi" satırını hiç eşleştiremedi ve
    // "refund" skill'i — config'te onu üreten TEK yer — hiçbir zaman gerekli sayılmadı.
    // Hiçbir test yakalamadı, çünkü mevcut router testi "şikayet" kullanıyor: iki
    // sözlükte de aynı yazılan nadir değerlerden biri.

    /// <summary>reasoning-system.md'deki `"intent": "a | b | c"` satırından değerleri çıkarır.</summary>
    private static List<string> ReasoningPromptIntentEnum()
    {
        var m = Regex.Match(Prompt("services/reasoning-system"),
            "\"intent\"\\s*:\\s*\"([^\"]+)\"");
        m.Success.Should().BeTrue("reasoning-system.md çıktı örneğinde \"intent\" satırı bulunmalı");

        return m.Groups[1].Value
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    [Fact]
    public void ReasoningPrompt_IntentEnum_MatchesWellKnownIntents()
    {
        ReasoningPromptIntentEnum().Should().BeEquivalentTo(WellKnown.Intents.LlmProduced,
            "prompt'un LLM'e sunduğu intent kümesi ile kodun tanıdığı küme birebir aynı olmalı; " +
            "sapan bir değer hiçbir hata vermeden tüm tam-string eşleşmelerini ıskalar");
    }

    [Fact]
    public void ReasoningPrompt_DoesNotMentionUnknownIntent()
    {
        // "bilinmiyor" yalnızca ReasoningResultParser'ın parse hatası fallback'i — LLM'e
        // geçerli bir seçenek olarak sunulursa model başarısızlığı kendi beyan edebilir.
        ReasoningPromptIntentEnum().Should().NotContain(WellKnown.Intents.Unknown);
    }

    [Fact]
    public void IntentSkillMap_Keys_AreAllKnownIntents()
    {
        // appsettings'teki IntentSkillMap anahtarları ReasoningResult.Intent ile tam-string
        // karşılaştırılır (SkillsBasedRouter.ExtractRequiredSkills). Sözlükte olmayan bir
        // anahtar ölü konfigürasyondur — o skill hiçbir zaman gerekli sayılmaz.
        using var settings = JsonDocument.Parse(File.ReadAllText(RepoPath("src/CustomerSupportBot.Api/appsettings.json")));

        var keys = settings.RootElement
            .GetProperty("Routing").GetProperty("IntentSkillMap")
            .EnumerateObject().Select(p => p.Name).ToList();

        keys.Should().NotBeEmpty();
        keys.Should().OnlyContain(k => WellKnown.Intents.LlmProduced.Contains(k),
            "her IntentSkillMap anahtarı LLM'in gerçekten üretebileceği bir intent olmalı");
    }

    // ─── Halka 4f: planning-agent.md'de ölü hint atfı kalmamalı ──────────────────

    [Fact]
    public void PlanningPrompt_DoesNotReferenceRemovedCompoundQueryHint()
    {
        // WorkflowMessageBuilder eskiden reasoning hint'ine "COMPOUND QUERY" bloğu
        // ekliyordu; blok kaldırıldı (gerçek decompose yolunda hiç tetiklenmiyordu —
        // CreateSubTaskReasoning SubTasks'ı boşaltır). Prompt hâlâ o nota koşullu
        // talimat verirse LLM hiç gelmeyecek bir sinyali beklemeye devam eder.
        const string marker = "COMPOUND QUERY";

        SourceOf("src/CustomerSupportBot.Adapters.Agents/WorkflowMessageBuilder.cs")
            .Should().NotContain($"\"{marker}", "kod bu hint metnini artık üretmiyor");
        Prompt("agents/planning-agent").Should().NotContain(marker,
            "planning-agent.md üretilmeyen bir hint'e koşullu talimat bağlamamalı");
    }

    // ─── Halka 5: Görsel mimari dokümanının tool matrisi ──────────────────────────
    // docs/agent-architecture.html tool↔ajan matrisini elle listeliyor. Bir tool eklenip
    // doküman güncellenmezse matris sessizce yanlışa döner — bu, admin panelindeki
    // HighRiskTools kopyasının senkronunu kaybetmesiyle aynı sapma sınıfı.

    [Fact]
    public void ArchitectureDoc_ToolMatrix_CoversEveryKnownTool()
    {
        var doc = SourceOf("docs/agent-architecture.html");

        var known = typeof(WellKnown.ToolNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        foreach (var tool in known)
            doc.Should().Contain(tool,
                $"'{tool}' WellKnown.ToolNames'te tanımlı; agent-architecture.html matrisinde de görünmeli");
    }

    [Fact]
    public void ArchitectureDoc_ClaimsCorrectToolAndApprovalCounts()
    {
        // Başlıktaki sayaçlar ("10 tool", "4 HITL onay kapısı") koddan doğrulanır.
        var doc = SourceOf("docs/agent-architecture.html");

        var toolCount = typeof(WellKnown.ToolNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Count(f => f.IsLiteral && f.FieldType == typeof(string));

        doc.Should().Contain($"<b>{toolCount}</b> tool",
            "başlıktaki tool sayısı WellKnown.ToolNames ile uyuşmalı");
        doc.Should().Contain($"<b>{WellKnown.HighRiskTools.Count}</b> HITL onay kapısı",
            "başlıktaki onay kapısı sayısı WellKnown.HighRiskTools ile uyuşmalı");
    }

    [Fact]
    public void ArchitectureDoc_GuardValues_MatchAppSettings()
    {
        // Doküman guard tablosunda ETKİN değerleri göstermeli — sınıf varsayılanlarını değil.
        // İlk yazımda TimeoutSeconds için sınıf varsayılanı (60) yazılmıştı; appsettings'teki
        // gerçek değer 180. Bu test o karışıklığı tekrarlanamaz kılar.
        var doc = SourceOf("docs/agent-architecture.html");
        using var settings = JsonDocument.Parse(File.ReadAllText(RepoPath("src/CustomerSupportBot.Api/appsettings.json")));

        var guards = settings.RootElement.GetProperty("WorkflowGuards");
        foreach (var key in new[] { "TimeoutSeconds", "MaxIterations", "MaxHandoffsPerAgent", "MaxDuplicateToolCalls" })
        {
            var value = guards.GetProperty(key).GetInt32();

            // Biçimden bağımsız eşleşme: HTML formatlayıcıları etiketleri ayrı satırlara
            // bölebiliyor, bu yüzden aradaki boşluk esnek bırakılır. (Tam-eşleşme kullanan
            // ilk sürüm, dosya yeniden biçimlendirildiğinde içerik doğru olduğu hâlde kırıldı.)
            Regex.IsMatch(doc, $@"<code>{Regex.Escape(key)}</code>\s*</td>\s*<td class=""num"">{value}</td>")
                .Should().BeTrue($"'{key}' dokümanda appsettings'teki etkin değeriyle ({value}) görünmeli");
        }
    }

    [Fact]
    public void ArchitectureDoc_SlaThresholds_MatchAppSettings()
    {
        // SLA eşikleri doküman tablosunda sayı olarak yazılı; config değişirse sunumdaki
        // rakam yanlışa döner. Guard değerleriyle aynı sapma sınıfı.
        var doc = SourceOf("docs/agent-architecture.html");
        using var settings = JsonDocument.Parse(File.ReadAllText(RepoPath("src/CustomerSupportBot.Api/appsettings.json")));
        var sla = settings.RootElement.GetProperty("Sla");

        foreach (var (group, key) in new[]
                 {
                     ("Approvals", "WarnAfterSeconds"), ("Approvals", "BreachAfterSeconds"),
                     ("Escalations", "WarnAfterSeconds"), ("Escalations", "BreachAfterSeconds")
                 })
        {
            var value = sla.GetProperty(group).GetProperty(key).GetInt32();
            doc.Should().Contain($">{value} sn<",
                $"SLA {group}.{key} ({value} sn) dokümanda görünmeli");
        }
    }

    [Fact]
    public void ArchitectureDoc_AgentCount_MatchesWellKnown()
    {
        var doc = SourceOf("docs/agent-architecture.html");
        doc.Should().Contain($"<b>{WellKnown.AgentNames.All.Length}</b> ajan");
    }

    [Fact]
    public void ArchitectureDoc_AllSectionsLiveInsideMain()
    {
        // YERLEŞİM SÖZLEŞMESİ. .wrap bir CSS grid'idir (geniş ekranda 200px ray + içerik).
        // Yalnızca header/nav/main/footer onun doğrudan çocuğu olmalı. Bir <section> yanlışlıkla
        // </main> DIŞINDA kalırsa grid onu kendi hücresine yerleştirir ve ray sütununun üstüne
        // biner — sayfa canlıda gözle görülür şekilde bozulur.
        //
        // Bu gerçekten yaşandı: 09-12 bölümleri <footer> öncesine eklenirken </main> dışında
        // kaldı. Etiket sayımı ve nesting kontrolü bunu YAKALAMADI; ikisi de geçerliydi.
        var doc = SourceOf("docs/agent-architecture.html");

        var mainOpen = doc.IndexOf("<main>", StringComparison.Ordinal);
        var mainClose = doc.IndexOf("</main>", StringComparison.Ordinal);
        mainOpen.Should().BeGreaterThan(0, "<main> bulunmalı");
        mainClose.Should().BeGreaterThan(mainOpen, "</main> <main>'den sonra gelmeli");

        foreach (Match m in Regex.Matches(doc, @"<section id=""([^""]+)"""))
        {
            m.Index.Should().BeInRange(mainOpen, mainClose,
                $"'{m.Groups[1].Value}' bölümü <main> içinde olmalı — dışarıda kalırsa grid yerleşimi bozulur");
        }
    }

    [Fact]
    public void ArchitectureDoc_EverySectionIsReachableFromNav()
    {
        // Yeni bölüm eklenip yan menüye link konmazsa sayfa gezilemez hâle gelir;
        // tersi (var olmayan id'ye link) ise ölü bağlantı üretir.
        var doc = SourceOf("docs/agent-architecture.html");

        var sectionIds = Regex.Matches(doc, @"<section id=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var navTargets = Regex.Matches(doc, @"<a href=""#([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

        navTargets.Should().BeSubsetOf(sectionIds, "her iç bağlantı var olan bir bölüme işaret etmeli");
        sectionIds.Should().BeSubsetOf(navTargets, "her bölümün yan menüde bir bağlantısı olmalı");
    }

    [Fact]
    public void ArchitectureDoc_IsSelfContained()
    {
        // Repo hiçbir yerde CDN kullanmıyor; doküman çevrimdışı ve dosya sisteminden
        // açıldığında da çalışmalı.
        var doc = SourceOf("docs/agent-architecture.html");

        Regex.IsMatch(doc, @"(src|href)\s*=\s*""https?://").Should().BeFalse(
            "agent-architecture.html harici script/stil/font yüklememeli");
    }

    private static string RepoPath(string repoRelativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, repoRelativePath)))
            dir = dir.Parent;

        dir.Should().NotBeNull($"repo kökü bulunamadı ({repoRelativePath} aranıyordu)");
        return Path.Combine(dir!.FullName, repoRelativePath);
    }

    private static string SourceOf(string repoRelativePath)
    {
        // Test çıktı klasöründen repo köküne çık (bin/Debug/net10.0 → 3 seviye + proje klasörü).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, repoRelativePath)))
            dir = dir.Parent;

        dir.Should().NotBeNull($"repo kökü bulunamadı ({repoRelativePath} aranıyordu)");
        return File.ReadAllText(Path.Combine(dir!.FullName, repoRelativePath));
    }
}
