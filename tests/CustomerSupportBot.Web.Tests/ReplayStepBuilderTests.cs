// Trace Replay adımlarının sırası. Uzman (specialist) değerlendirmesi ajanın ÇIKTISINDAN üretilir; kaydında zaman
// alanı yoktur. Eskiden trace'in başlangıç zamanı kullanılıyor ve kart ilk ajandan (PlanningAgent) bile önce
// görünüyordu (gerçekte ComplaintAgent bittikten sonra oluşur).

using System.Text.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Tests;

public class ReplayStepBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 22, 21, 10, 40, 760, TimeSpan.Zero);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TraceAgentVisit Visit(string name, int startMs, int durationMs) =>
        new(name, T0.AddMilliseconds(startMs), durationMs, "çıktı");

    private static TraceDetail Trace(List<TraceAgentVisit> visits, params string[] specialistJson) => new()
    {
        TraceId = "t1", SessionId = "s1", UserQuery = "Siparişim gecikti", StartedAt = T0,
        CompletedAt = T0.AddSeconds(10),
        Reasoning = Json("""{"intent":"şikayet"}"""),
        Planning = Json("""{"selectedAgent":"ComplaintAgent"}"""),
        AgentVisits = visits,
        SpecialistReasonings = specialistJson.Select(Json).ToList()
    };

    private static List<string> Titles(TraceDetail trace) => ReplayStepBuilder.Build(trace).Select(s => s.Title).ToList();

    [Fact]
    public void SpecialistReasoning_ComesRightAfterItsAgentFinishes_NotAtTheTraceStart()
    {
        var trace = Trace(
        [
            Visit("PlanningAgent_b58d", 7, 3553),
            Visit("ComplaintAgent_f183", 3561, 3493),
            Visit("ResponseAgent_5964", 7060, 2300)
        ], """{"agentName":"ComplaintAgent"}""");

        Titles(trace).Should().Equal(
            "Müşteri mesajı", "Ön analiz", "→ ComplaintAgent",
            "PlanningAgent_b58d", "ComplaintAgent_f183", "Değerlendirme: ComplaintAgent", "ResponseAgent_5964",
            "Asistanın yanıtı");
        ReplayStepBuilder.Build(trace).Single(s => s.Title == "Değerlendirme: ComplaintAgent").Time
            .Should().Be(T0.AddMilliseconds(3561 + 3493), "ajanın bitiş zamanı");
    }

    [Fact]
    public void SameAgentVisitedTwice_EachReasoningFollowsItsOwnVisit()
    {
        // Yeniden planlama: ComplaintAgent iki kez çalıştı, iki değerlendirme var — sırayla eşleşir.
        var trace = Trace(
        [
            Visit("ComplaintAgent_a", 0, 1000),
            Visit("PlanningAgent_b", 1100, 500),
            Visit("ComplaintAgent_c", 1700, 1000)
        ], """{"agentName":"ComplaintAgent","resultNotes":"ilk"}""", """{"agentName":"ComplaintAgent","resultNotes":"ikinci"}""");

        Titles(trace).Skip(3).Take(5).Should().Equal(
            "ComplaintAgent_a", "Değerlendirme: ComplaintAgent", "PlanningAgent_b", "ComplaintAgent_c", "Değerlendirme: ComplaintAgent");
    }

    [Fact]
    public void RecordedAt_WhenPresent_IsUsed()
    {
        var trace = Trace([Visit("ComplaintAgent_a", 0, 1000), Visit("ResponseAgent_b", 2000, 500)],
            $$"""{"agentName":"ComplaintAgent","recordedAt":"{{T0.AddMilliseconds(2600):O}}"}""");

        Titles(trace).Skip(3).Take(3).Should().Equal("ComplaintAgent_a", "ResponseAgent_b", "Değerlendirme: ComplaintAgent");
    }

    [Fact]
    public void ReasoningWithoutAMatchingVisit_FallsBackToTheTraceStart()
    {
        var trace = Trace([Visit("ResponseAgent_b", 2000, 500)], """{"agentName":"OrderAgent"}""");

        ReplayStepBuilder.Build(trace).Single(s => s.Title == "Değerlendirme: OrderAgent").Time.Should().Be(T0);
    }
}
