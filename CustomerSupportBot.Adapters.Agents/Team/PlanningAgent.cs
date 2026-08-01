// Adapters.Agents/Team/PlanningAgent.cs
// Müşteri talebini analiz eder, yapılandırılmış bir plan (JSON) üretir ve uygun
// specialist ajana yönlendirir. Tool'u yoktur — yalnızca yönlendirme kararı verir.

using System.Text.Json;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class PlanningAgent : SupportAgentBase
{
    // OpenAI/Azure OpenAI response_format=json_schema (strict) ile PlanningAgent'ın çıktısını
    // PlanningResult şemasına zorluyor — model artık markdown fence'i unutamaz veya alan
    // atlayamaz. Anthropic bridge'i (Microsoft.Agents.AI.Anthropic) ChatOptions.ResponseFormat'ı
    // hiç okumuyor (decompile ile doğrulandı) — o path'te bu sessizce no-op olur, prompt'taki
    // manuel JSON talimatı tek güvence olarak kalır. Bu yüzden PlanningResultParser'ın fence
    // temizleme + alan bazlı defensive parse mantığı KALDIRILMADI: response_format'ı honor
    // etmeyen/edemeyen provider'larda hâlâ tek çalışan yol o.
    private static readonly JsonSerializerOptions PlanningSchemaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PlanningAgent(IChatClient chatClient, IPromptRepository prompts)
        : base(BuildInner(chatClient, prompts))
    {
    }

    private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
        => new(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = WellKnown.AgentNames.Planning,
                Description = "Müşteri destek görevlerini planlayan ve uygun ajanlara yönlendiren bir ajandır.",
                ChatOptions = new ChatOptions
                {
                    Instructions = prompts.Get("agents/planning-agent"),
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<PlanningResult>(PlanningSchemaJsonOptions)
                }
            });

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi — kullanıcı sorgusu,
    /// reasoning hint'i ve ENTITY EXTRACTION hint'i burada görülebilir.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>response.Text</c> — üretilen plan JSON'u
    /// (selectedAgent, detectedIntent, intentConfidence, needsClarification…).
    /// Routing kararı bu çıktıdan parse edilir (bkz. PlanRoutingStrategy).
    /// </summary>
    protected override void OnAfterRun(AgentResponse response)
    {
        _ = response.Text;
    }
}
