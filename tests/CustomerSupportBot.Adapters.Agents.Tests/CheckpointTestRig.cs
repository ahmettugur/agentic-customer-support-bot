// Checkpoint testleri için ortak donanım: GERÇEK bir MAF GroupChat workflow'u, betikli (sahte) bir LLM ile.
// "Çökme", belirli bir LLM çağrısında koşunun iptal edilmesiyle simüle edilir; "yeni süreç" ise sıfırdan kurulan
// factory + workflow'dur (aynı checkpoint deposuyla).

using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Application.Services.UiHint;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Agents.Tests;

internal sealed class CheckpointTestRig
{
    /// <summary>Planlama → ProductAgent (tool çağrısı + yansıma) → ResponseAgent akışını betikler.</summary>
    internal sealed class ScriptedLlm(Action<int>? onCall = null) : IChatClient
    {
        private int _calls;
        public int Calls => _calls;
        public List<string> Instructions { get; } = [];

        public async Task<ModelResponse> GetResponseAsync(IEnumerable<ChatMessage> input, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var n = Interlocked.Increment(ref _calls);
            lock (Instructions) Instructions.Add(options?.Instructions ?? "");
            onCall?.Invoke(n);
            await Task.Delay(1, cancellationToken);
            var messages = input.ToList();
            if (options?.Instructions == "agents/planning-agent")
                return new(new ChatMessage(ChatRole.Assistant, "{\"selectedAgent\":\"ProductAgent\",\"needsClarification\":false}"));
            if (options?.Instructions == "agents/product-agent")
            {
                if (!messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any())
                    return new(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent(Guid.NewGuid().ToString(), WellKnown.ToolNames.ProductList, new Dictionary<string, object?>())]));
                return new(new ChatMessage(ChatRole.Assistant, "{\"postToolReflection\":{\"status\":\"done\",\"taskComplete\":true}}"));
            }
            return new(new ChatMessage(ChatRole.Assistant, "Fresh answer\nTERMINATE: reason=completed"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates()) yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    public static (ICustomerSupportToolsService Tools, ApprovalGateService Gate, ApprovalContextAccessor Context, IPromptRepository Prompts, UiHintEmitter Emitter)
        Dependencies()
    {
        var ctx = new ApprovalContextAccessor();
        var emitter = new UiHintEmitter(ctx);
        var products = Substitute.For<IProductCatalogRepository>();
        products.GetSelectableCategories().Returns(new List<string> { "A", "B" });
        var productTools = new ProductToolsService(products, emitter);
        var tools = Substitute.For<ICustomerSupportToolsService>();
        tools.ProductListTool(Arg.Any<string?>()).Returns(call => productTools.ProductListTool(call.Arg<string?>()));
        var options = Options.Create(new ApprovalOptions { Enabled = false });
        var sink = Substitute.For<IEscalationSink>();
        var gate = new ApprovalGateService(Substitute.For<IApprovalQueue>(), options, sink, ctx, tools, new EscalationPolicyService(sink, options));
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return (tools, gate, ctx, prompts, emitter);
    }

    public static AgentTeamFactory Factory(IChatClient client, int maxIterations = 10)
    {
        var (tools, gate, ctx, prompts, _) = Dependencies();
        return new AgentTeamFactory(client, prompts, gate, tools,
            new WorkflowGuardOptions { MaxIterations = maxIterations, TimeoutSeconds = 30 }, NullLoggerFactory.Instance, ctx);
    }
}
