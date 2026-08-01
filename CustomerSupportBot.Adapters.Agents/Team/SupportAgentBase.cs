// Adapters.Agents/Team/SupportAgentBase.cs
// Takımdaki her ajanın ortak iskeleti. Her ajan kendi prompt'unu, adını, açıklamasını
// ve tool listesini KENDİ sınıfında tanımlar; AgentTeamFactory yalnızca örnekler.
//
// ChatClientAgent sealed olduğu için ondan türetilemez; SDK'nın bu amaç için sunduğu
// DelegatingAIAgent taban sınıfı kullanılır (Name/Id/Description otomatik iç ajana yönlenir,
// davranış birebir korunur).
//
// Debug: LLM'in kendi akıl yürütmesine adım adım girilemez, ama çağrının GİRDİSİ ve
// ÇIKTISI burada yakalanır. OnBeforeRun/OnAfterRun her ajanda AYRI implemente edilir —
// böylece breakpoint yalnızca o ajanın turunda durur, altısında birden değil.
//
// ÖNEMLİ: RunCoreAsync VE RunCoreStreamingAsync'in İKİSİ de override edilmeli.
// WorkflowRunner her turda TurnToken(emitEvents: true) gönderiyor — bu, GroupChat'in
// AIAgentHostExecutor'ının ajanı HER ZAMAN streaming yoldan (_agent.RunStreamingAsync)
// çağırması demek, RunAsync/RunCoreAsync değil. Sadece RunCoreAsync override edilseydi
// (önceki hâli) bu hook'lar gerçek çalışan sistemde HİÇ TETİKLENMEZDİ — DelegatingAIAgent'ın
// varsayılan RunCoreStreamingAsync'i doğrudan iç ajana geçer, sessizce.

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal abstract class SupportAgentBase : DelegatingAIAgent
{
    protected SupportAgentBase(ChatClientAgent innerAgent) : base(innerAgent)
    {
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        OnBeforeRun(messageList);

        var response = await base.RunCoreAsync(messageList, session, options, cancellationToken)
            .ConfigureAwait(false);

        OnAfterRun(response);

        return response;
    }

    /// <summary>
    /// Gerçek çalışan sistemde kullanılan yol budur (bkz. sınıf başı yorum). Update'leri
    /// olduğu gibi yield edip ayrıca biriktiriyoruz; akış bitince biriken update'lerden
    /// <see cref="AgentResponseExtensions.ToAgentResponse(IEnumerable{AgentResponseUpdate})"/>
    /// ile RunCoreAsync ile aynı şekle sahip bir AgentResponse kurup OnAfterRun'a veriyoruz —
    /// böylece iki yoldaki debug hook'ları tutarlı davranıyor.
    /// </summary>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        OnBeforeRun(messageList);

        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in base.RunCoreStreamingAsync(messageList, session, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            updates.Add(update);
            yield return update;
        }

        OnAfterRun(updates.ToAgentResponse());
    }

    /// <summary>BREAKPOINT: LLM'e gönderilmek üzere olan tam mesaj listesi.</summary>
    protected abstract void OnBeforeRun(IReadOnlyList<ChatMessage> messages);

    /// <summary>BREAKPOINT: LLM'in bu ajan için ürettiği tam yanıt.</summary>
    protected abstract void OnAfterRun(AgentResponse response);

    /// <summary>Yanıttaki tool çağrılarını (tool adı + argümanlar) çıkarır.</summary>
    protected static List<FunctionCallContent> ToolCalls(AgentResponse response) =>
        response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();

    /// <summary>Yanıttaki tool sonuçlarını çıkarır.</summary>
    protected static List<FunctionResultContent> ToolResults(AgentResponse response) =>
        response.Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
}
