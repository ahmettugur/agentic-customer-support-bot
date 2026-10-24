// Müşteriye giden "düşünce süreci": yalnızca arayüzün gösterdiği alanlar, temizlenmiş metinle. Reasoning'in iç alanları
// (nextAction, rationale, varsayımlar, decisionReason, sanity uyarıları, doğrulanmış kimlikler, adım önkoşulları) ağa HİÇ çıkmaz.
//
// Arka plan: tüm ReasoningResult olduğu gibi SSE'ye ve /chat yanıtına yazılıyordu; çıktı guardrail'i yalnızca response_complete'i
// tarıyordu, yani bu yol korumasızdı.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class CustomerReasoningViewTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static ReasoningResult Full() => new()
    {
        Analysis = "Kullanıcı 1057 numaralı siparişinin durumunu soruyor.",
        Intent = WellKnown.Intents.OrderInquiry,
        Confidence = "yüksek",
        ConfidenceScore = 0.9,
        RequiredInfo = ["sipariş_numarası"],
        Rationale = "GİZLİ-RATIONALE",
        NextAction = "OrderAgent'e yönlendir GİZLİ-NEXT",
        DecisionReason = "GİZLİ-DECISION",
        Assumptions = ["GİZLİ-VARSAYIM"],
        Sentiment = "angry",
        SanityIssues = [new ReasoningIssue { Code = "GİZLİ-SANITY", Message = "GİZLİ-SANITY-MSG" }],
        VerifiedEntities = new VerifiedEntities
        {
            CustomerId = new VerifiedEntity { Value = "GİZLİ-KİMLİK", Verification = EntityVerification.Verified }
        },
        Steps =
        [
            new ReasoningStep
            {
                Order = 1, Description = "1057 siparişini sorgula", Action = "call_tool", Grounding = "DB", Confidence = 0.95,
                Premise = "GİZLİ-PREMISE", AlternativeRejected = "GİZLİ-ALTERNATİF"
            }
        ],
        SubTasks =
        [
            new SubTask { Order = 1, Description = "durum sorgula", TargetAgent = WellKnown.AgentNames.Order, Intent = "x",
                          Entities = new() { ["order_id"] = "GİZLİ-ENTITY" } },
            new SubTask { Order = 2, Description = "şikayet aç", TargetAgent = WellKnown.AgentNames.Complaint, Intent = "y" }
        ]
    };

    private static string Json(CustomerReasoningView v) => JsonSerializer.Serialize(v, Web);

    [Fact]
    public void NoInternalFieldReachesTheWire()
    {
        var json = Json(CustomerReasoningView.From(Full(), s => s));

        json.Should().NotContain("GİZLİ");
        foreach (var internalField in new[] { "nextAction", "rationale", "assumptions", "decisionReason", "sanityIssues", "verifiedEntities",
                                              "premise", "alternativeRejected", "sentiment", "confidenceScore", "entities", "dependencies" })
            json.Should().NotContain($"\"{internalField}\"", $"'{internalField}' müşteriye gitmemeli");
    }

    [Fact]
    public void TheFieldsTheChatPanelRenders_AreKept_WithTheNamesItReads()
    {
        using var doc = JsonDocument.Parse(Json(CustomerReasoningView.From(Full(), s => s)));
        var root = doc.RootElement;

        root.GetProperty("analysis").GetString().Should().Contain("1057");
        root.GetProperty("intent").GetString().Should().Be(WellKnown.Intents.OrderInquiry);
        root.GetProperty("confidence").GetString().Should().Be("yüksek");
        root.GetProperty("requiredInfo")[0].GetString().Should().Be("sipariş_numarası");
        var step = root.GetProperty("steps")[0];
        step.GetProperty("order").GetInt32().Should().Be(1);
        step.GetProperty("description").GetString().Should().Be("1057 siparişini sorgula");
        step.GetProperty("action").GetString().Should().Be("call_tool");
        step.GetProperty("grounding").GetString().Should().Be("DB");
        step.GetProperty("confidence").GetDouble().Should().Be(0.95);
        root.GetProperty("subTasks").GetArrayLength().Should().Be(2);
        root.GetProperty("subTasks")[1].GetProperty("targetAgent").GetString().Should().Be(WellKnown.AgentNames.Complaint);
        root.GetProperty("isFallback").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void EveryFreeTextField_GoesThroughTheSanitizer()
    {
        var r = Full();
        r.Analysis = "analiz KIRLI"; r.Steps[0].Description = "adım KIRLI"; r.RequiredInfo = ["bilgi KIRLI"]; r.SubTasks[0].Description = "alt görev KIRLI";

        var v = CustomerReasoningView.From(r, s => s.Replace("KIRLI", "[gizlendi]"));

        Json(v).Should().NotContain("KIRLI");
        v.Analysis.Should().Be("analiz [gizlendi]");
        v.Steps[0].Description.Should().Be("adım [gizlendi]");
        v.RequiredInfo[0].Should().Be("bilgi [gizlendi]");
        v.SubTasks[0].Description.Should().Be("alt görev [gizlendi]");
    }

    [Fact]
    public void TheResult_IsBounded()
    {
        var r = Full();
        r.Analysis = new string('a', 5000);
        r.Steps = Enumerable.Range(1, 30).Select(i => new ReasoningStep { Order = i, Description = new string('d', 2000) }).ToList();
        r.SubTasks = Enumerable.Range(1, 20).Select(i => new SubTask { Order = i, Description = "x", TargetAgent = "OrderAgent" }).ToList();
        r.RequiredInfo = Enumerable.Range(1, 50).Select(i => $"alan{i}").ToList();

        var v = CustomerReasoningView.From(r, s => s);

        v.Analysis.Length.Should().BeLessThanOrEqualTo(CustomerReasoningView.MaxAnalysisLength + 1);
        v.Steps.Count.Should().BeLessThanOrEqualTo(CustomerReasoningView.MaxSteps);
        v.Steps[0].Description.Length.Should().BeLessThanOrEqualTo(CustomerReasoningView.MaxTextLength + 1);
        v.SubTasks.Count.Should().BeLessThanOrEqualTo(CustomerReasoningView.MaxSubTasks);
        v.RequiredInfo.Count.Should().BeLessThanOrEqualTo(CustomerReasoningView.MaxRequiredInfo);
    }

    [Fact]
    public void AFallbackResult_IsFlagged_AndNeverThrows()
    {
        var v = CustomerReasoningView.From(new ReasoningResult { IsFallback = true, Analysis = "kullanılamıyor" }, s => s);

        v.IsFallback.Should().BeTrue();
        v.Steps.Should().BeEmpty();
        v.SubTasks.Should().BeEmpty();
    }

    [Fact]
    public void ASanitizerThatThrows_NeverBreaksTheTurn_AndLeaksNothing()
    {
        var v = CustomerReasoningView.From(Full(), _ => throw new InvalidOperationException("boom"));

        v.Analysis.Should().BeEmpty("temizlenemeyen metin ham halde gönderilmez");
        v.Steps.Should().OnlyContain(s => s.Description.Length == 0);
    }
}

public class ReasoningAnalysisStreamerTests
{
    private const string Raw = """{"analysis":"Kullanıcı \"1057\" siparişini soruyor.\nAyrıca teşekkür ediyor.","steps":[{"order":1,"description":"x"}],"intent":"genel"}""";
    private const string Expected = "Kullanıcı \"1057\" siparişini soruyor.\nAyrıca teşekkür ediyor.";

    private static string FeedAll(IEnumerable<string> chunks)
    {
        var streamer = new ReasoningAnalysisStreamer();
        return string.Concat(chunks.Select(streamer.Feed));
    }

    [Fact]
    public void OnlyTheAnalysisText_IsEmitted_DecodedAndWithoutJson() => FeedAll([Raw]).Should().Be(Expected);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void TheSameResult_RegardlessOfHowTheStreamIsChunked(int size) =>
        FeedAll(Enumerable.Range(0, (Raw.Length + size - 1) / size).Select(i => Raw.Substring(i * size, Math.Min(size, Raw.Length - i * size))))
            .Should().Be(Expected);

    [Fact]
    public void NothingIsEmittedBeforeTheAnalysisValueStarts()
    {
        var s = new ReasoningAnalysisStreamer();

        s.Feed("{\"anal").Should().BeEmpty();
        s.Feed("ysis\": ").Should().BeEmpty();
        s.Feed("\"Merhaba").Should().Be("Merhaba");
    }

    [Fact]
    public void UnicodeEscapes_AreDecoded_EvenWhenSplitAcrossChunks()
    {
        var s = new ReasoningAnalysisStreamer();

        var text = s.Feed("{\"analysis\":\"\\u00FC") + s.Feed("r\\u00FCn\"}");

        text.Should().Be("ürün");
    }

    [Fact]
    public void AfterTheClosingQuote_NothingMoreIsEmitted_NotEvenOtherFieldsText()
    {
        var s = new ReasoningAnalysisStreamer();

        s.Feed("{\"analysis\":\"kısa\",\"nextAction\":\"GİZLİ").Should().Be("kısa");
        s.Feed("-DEVAM\",\"rationale\":\"GİZLİ\"}").Should().BeEmpty();
    }

    [Fact]
    public void WithoutAnAnalysisKey_NothingIsEverEmitted() =>
        FeedAll(["{\"intent\":\"genel\",", "\"nextAction\":\"OrderAgent'e yönlendir\"}"]).Should().BeEmpty();

    [Fact]
    public void AnEscapeThatNeverCompletes_IsHeldBack_NotEmittedHalf()
    {
        var s = new ReasoningAnalysisStreamer();

        s.Feed("{\"analysis\":\"abc\\").Should().Be("abc");
        s.Feed("n").Should().Be("\n");
    }
}
