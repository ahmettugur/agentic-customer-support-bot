// Adapters.Agents/Team/ResponseAgent.cs
// Turun son ajanı: specialist çıktısını kullanıcıya sunulacak nihai metne dönüştürür
// ve "TERMINATE: reason=..." işaretiyle workflow'u sonlandırır. Tool'u yoktur.
//
// Not: Kullanıcıya akan gerçek zamanlı token'lar bu ajanın çıktısıdır
// (bkz. WorkflowRunner.ApplyTraceEvent → AgentResponseUpdateEvent dalı).

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class ResponseAgent : SupportAgentBase
{
    public ResponseAgent(IChatClient chatClient, IPromptRepository prompts)
        : base(BuildInner(chatClient, prompts))
    {
    }

    private static ChatClientAgent BuildInner(IChatClient chatClient, IPromptRepository prompts)
        => new(
            chatClient,
            instructions: prompts.Get("agents/response-agent"),
            name: WellKnown.AgentNames.Response,
            description: "Yanıtları biçimlendirir ve kullanıcıya iletir.");

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi — specialist'in ürettiği
    /// ham çıktı (tool sonuçları + reasoning JSON) burada görülür.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>response.Text</c> — kullanıcıya gidecek nihai metin
    /// ve sonundaki TERMINATE işareti (bu işaret akıştan filtrelenir, bkz.
    /// WorkflowRunner.ResponseStreamFilter).
    /// </summary>
    protected override void OnAfterRun(AgentResponse response)
    {
        _ = response.Text;
    }
}
