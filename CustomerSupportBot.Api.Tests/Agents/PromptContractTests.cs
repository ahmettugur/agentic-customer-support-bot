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

        // Kod tarafı: WorkflowRunner.BuildReasoningSummaryHint aynı etiketi üretir.
        // Metot instance olduğu ve tüm bağımlılıkları gerektirdiği için (Docker'lı fixture)
        // burada kaynak dosya üzerinden doğrulanır — amaç iki ucun senkronunu korumak.
        SourceOf("CustomerSupportBot.Adapters.Agents/WorkflowRunner.cs")
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
        using var settings = JsonDocument.Parse(File.ReadAllText(RepoPath("CustomerSupportBot.Api/appsettings.json")));

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
        using var settings = JsonDocument.Parse(File.ReadAllText(RepoPath("CustomerSupportBot.Api/appsettings.json")));
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
