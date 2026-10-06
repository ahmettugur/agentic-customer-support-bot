// Models/ReplayStepBuilder.cs
// Trace Replay adımları — TraceDetail'den istemci tarafında üretilir (Replay.razor). Saf sınıf, birim testli.

using System.Text.Json;

namespace CustomerSupportBot.Web.Models;

public static class ReplayStepBuilder
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Sıra: soru → ön analiz → yönlendirme → (ajan ziyaretleri, uzman değerlendirmeleri, araç çağrıları zamana
    /// göre) → yanıt.
    ///
    /// <para>
    /// 🐞 Uzman değerlendirmesi ajanın çıktısından üretilir ve kaydında zaman alanı yoktur. Eskiden yerine trace'in
    /// başlangıç zamanı konuyordu: kart ilk ajandan (PlanningAgent) bile önce görünüyordu, oysa ajan BİTTİKTEN sonra
    /// oluşur. Artık aynı ajanın sıradaki ziyaretinin bitiş zamanına yerleşir (yeniden planlamada aynı ajan birden çok
    /// kez çalışabilir — değerlendirmeler ziyaretlerle sırayla eşleşir). <c>recordedAt</c> varsa o kullanılır;
    /// eşleşen ziyaret yoksa eski davranış (trace başlangıcı).
    /// </para>
    /// </summary>
    public static List<ReplayStep> Build(TraceDetail trace)
    {
        var steps = new List<ReplayStep>
        {
            new("init", trace.StartedAt, "Kullanıcı Sorusu", new ReplayInitPayload(trace.TraceId, trace.SessionId, trace.UserQuery))
        };

        if (trace.Reasoning.HasValue)
            steps.Add(new ReplayStep("reasoning", trace.StartedAt, "Pre-analysis Reasoning",
                new ReplayJsonPayload(JsonSerializer.Serialize(trace.Reasoning, Pretty))));

        if (trace.Planning.HasValue)
        {
            var plan = trace.Planning.Value;
            var title = "Yönlendirme";
            if (TryGetProp(plan, "selectedAgent", out var sa) && sa.ValueKind is not JsonValueKind.Null)
                title = $"→ {sa.GetString()}";
            else if (TryGetProp(plan, "detectedIntent", out var di) && di.ValueKind is not JsonValueKind.Null)
                title = $"→ {di.GetString()}";
            steps.Add(new ReplayStep("planning", trace.StartedAt, title, new ReplayJsonPayload(JsonSerializer.Serialize(plan, Pretty))));
        }

        var events = new List<(DateTimeOffset T, string Kind, string Title, ReplayStepPayload Payload)>();

        foreach (var v in trace.AgentVisits)
            events.Add((v.StartedAt, "agent", v.AgentName ?? "?", new ReplayAgentPayload(v.AgentName, v.DurationMs, v.Output)));

        var usedVisits = new HashSet<int>();
        foreach (var sr in trace.SpecialistReasonings)
        {
            var agentName = TryGetProp(sr, "agentName", out var an) && an.ValueKind is not JsonValueKind.Null
                ? an.GetString() ?? "?"
                : "?";
            var t = TryGetProp(sr, "recordedAt", out var ra) && ra.ValueKind is not JsonValueKind.Null
                    && DateTimeOffset.TryParse(ra.GetString(), out var parsed)
                ? parsed
                : FinishOfNextVisit(trace.AgentVisits, agentName, usedVisits) ?? trace.StartedAt;
            events.Add((t, "reasoning", $"Specialist: {agentName}", new ReplayJsonPayload(JsonSerializer.Serialize(sr, Pretty))));
        }

        foreach (var tc in trace.ToolCalls)
            events.Add((tc.InvokedAt ?? trace.StartedAt, "tool", tc.ToolName ?? "?",
                new ReplayToolPayload(tc.ToolName, tc.AgentName, tc.Success, tc.ParametersSummary, tc.ResultSummary)));

        // Kararlı sıralama: eşit zamanda ekleme sırası korunur (ajan kartı kendi değerlendirmesinden önce).
        foreach (var (t, kind, title, payload) in events.OrderBy(e => e.T))
            steps.Add(new ReplayStep(kind, t, title, payload));

        steps.Add(new ReplayStep("final", trace.CompletedAt ?? trace.StartedAt, "Bot Yanıtı",
            new ReplayFinalPayload(trace.TerminationReason, trace.DurationMs, trace.IterationCount, trace.Error, trace.FinalResponse)));
        return steps;
    }

    /// <summary>
    /// Ajanın henüz bir değerlendirmeyle eşleşmemiş ilk ziyaretinin bitiş zamanı. Ziyaret adları örnek
    /// soneki taşır (<c>ComplaintAgent_f183…</c>), değerlendirmedeki ad taşımaz (<c>ComplaintAgent</c>).
    /// </summary>
    private static DateTimeOffset? FinishOfNextVisit(IReadOnlyList<TraceAgentVisit> visits, string agentName, HashSet<int> used)
    {
        for (var i = 0; i < visits.Count; i++)
        {
            var name = visits[i].AgentName;
            if (used.Contains(i) || name is null) continue;
            if (name != agentName && !name.StartsWith(agentName + "_", StringComparison.Ordinal)) continue;
            used.Add(i);
            return visits[i].StartedAt.AddMilliseconds(visits[i].DurationMs ?? 0);
        }
        return null;
    }

    /// <summary>Büyük/küçük harf duyarsız özellik arama: tam ad, PascalCase, camelCase.</summary>
    private static bool TryGetProp(JsonElement el, string key, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty(key, out value)) return true;
            var pascal = key.Length > 0 ? char.ToUpper(key[0]) + key[1..] : key;
            if (el.TryGetProperty(pascal, out value)) return true;
            var camel = key.Length > 0 ? char.ToLower(key[0]) + key[1..] : key;
            if (el.TryGetProperty(camel, out value)) return true;
        }
        value = default;
        return false;
    }
}
