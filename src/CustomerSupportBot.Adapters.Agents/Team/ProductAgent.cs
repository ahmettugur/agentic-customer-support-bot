// Adapters.Agents/Team/ProductAgent.cs
// Ürün sorgularını yanıtlar (tek ürün sorgulama + katalog/kategori listeleme).
// Tüm tool'ları salt-okunur olduğu için compound query'lerde paralel çalıştırılabilir
// (bkz. WellKnown.AgentNames.ReadOnly / ParallelExecutionOptions.IsReadOnly).

using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Team;

internal sealed class ProductAgent : SupportAgentBase
{
    public ProductAgent(
        IChatClient chatClient,
        IPromptRepository prompts,
        ICustomerSupportToolsService tools)
        : base(BuildInner(chatClient, prompts, tools))
    {
    }

    private static ChatClientAgent BuildInner(
        IChatClient chatClient,
        IPromptRepository prompts,
        ICustomerSupportToolsService tools)
        => new(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = WellKnown.AgentNames.Product,
                Description = "Ürün sorgularını yanıtlar.",
                ChatOptions = new ChatOptions
                {
                    Instructions = prompts.Get("agents/product-agent"),
                    Tools = [
                        AIFunctionFactory.Create(tools.ProductInquiryTool, new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductInquiry }),
                        AIFunctionFactory.Create(tools.ProductListTool,    new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductList })
                    ],
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(
                        SpecialistReasoningSchemaOptions.CamelCase)
                }
            });

    /// <summary>
    /// BREAKPOINT BURAYA: LLM'e gönderilen tam mesaj listesi.
    /// </summary>
    protected override void OnBeforeRun(IReadOnlyList<ChatMessage> messages)
    {
        _ = messages;
    }

    /// <summary>
    /// BREAKPOINT BURAYA: <c>toolCalls</c> — product_inquiry_tool / product_list_tool
    /// hangi argümanlarla çağrıldı, <c>toolResults</c> — sonucu (UI hint'leri ör.
    /// category_picker bu sonuçtan üretilir), <c>response.Text</c> — ajanın metni.
    /// </summary>
    protected override void OnAfterRun(AgentResponse response)
    {
        var toolCalls = ToolCalls(response);
        var toolResults = ToolResults(response);

        _ = toolCalls;
        _ = toolResults;
        _ = response.Text;
    }
}
