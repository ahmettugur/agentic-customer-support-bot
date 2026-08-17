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
    public async Task FinalizeAsync(
        ReasoningTrace trace,
        AgentSession? session,
        string query,
        string result,
        string terminationReason)
    {
        await _approvalGate.ProcessPendingEscalationsAsync(trace, query, result);
        PopulateAgentVisitOutputs(trace, result);
        WriteEpisodicMemorySafe(trace, query, result, session?.State.AuthenticatedCustomerId);
        await UpdateCustomerProfileSafeAsync(session, trace, query, result);

        _traceStore.Complete(trace.TraceId,
            terminationReason: terminationReason,
            finalResponse: result);
    }

    private static void PopulateAgentVisitOutputs(ReasoningTrace trace, string finalResult)
    {
        const int MaxLen = 1500;
        static string Truncate(string s) => s.Length <= MaxLen ? s : s[..MaxLen] + "…";

        foreach (var visit in trace.AgentVisits)
        {
            if (!string.IsNullOrWhiteSpace(visit.Output)) continue;

            var name = visit.AgentName ?? "";
            var baseName = name.Split('_', 2)[0];

            string? output = null;

            if (baseName.StartsWith("Planning", StringComparison.OrdinalIgnoreCase) && trace.Planning != null)
            {
                output = JsonSerializer.Serialize(trace.Planning, PrettyJson);
            }
            else if (baseName.StartsWith("Response", StringComparison.OrdinalIgnoreCase))
            {
                output = finalResult;
            }
            else if (trace.SpecialistReasonings.Count > 0)
            {
                var matching = trace.SpecialistReasonings
                    .Where(s => string.Equals(s.AgentName, baseName, StringComparison.OrdinalIgnoreCase)
                             || s.AgentName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
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
    {
        if (_semanticMemory is null || !_semanticMemory.Enabled) return;
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(response)) return;

        var sessionId = trace.SessionId;
        var traceId = trace.TraceId;
        var intent = trace.Reasoning?.Intent;
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

    private async Task UpdateCustomerProfileSafeAsync(AgentSession? session, ReasoningTrace trace, string query, string response)
    {
        if (_profileService is null) return;
        // AuthenticatedCustomerId (JWT) — State.CustomerId DEĞİL: aksi halde kullanıcı
        // "ben 1008'im" diyerek bu konuşmayı BAŞKA bir müşterinin kalıcı profiline
        // yazdırabilir (profil özeti/ilgi alanları/dil tercihi kalıcı olarak bozulur).
        var customerId = session?.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return;

        var intent = trace.Reasoning?.Intent;
        var logger = _loggerFactory.CreateLogger<TurnFinalizer>();

        try
        {
            await _profileService.RecordInteractionAsync(
                customerId: customerId,
                userQuery: query,
                botResponse: response,
                intent: intent,
                rating: null,
                isNewSession: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Customer profile güncellemesi başarısız (customerId={Id})", customerId);
        }
    }
}
