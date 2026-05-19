using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.AI.Chat;

namespace CustomerSupportBot.Api.Tests.Services;

public class AiClientFactoryTests
{
    [Fact]
    public void OpenAI_NoApiKey_Throws()
    {
        var opts = new AiOptions { Provider = AiProvider.OpenAI, OpenAI = { Model = "gpt-x" } };
        Action act = () => AiClientFactory.CreateStandardChatClient(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*OpenAI:ApiKey*");
    }

    [Fact]
    public void AzureOpenAI_NoEndpoint_Throws()
    {
        var opts = new AiOptions { Provider = AiProvider.AzureOpenAI, AzureOpenAI = { Deployment = "d" } };
        Action act = () => AiClientFactory.CreateStandardChatClient(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Endpoint*");
    }

    [Fact]
    public void AzureOpenAI_NoApiKey_Throws()
    {
        var opts = new AiOptions
        {
            Provider = AiProvider.AzureOpenAI,
            AzureOpenAI = { Endpoint = "https://example.openai.azure.com", Deployment = "d" }
        };
        Action act = () => AiClientFactory.CreateStandardChatClient(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void Anthropic_NoApiKey_Throws()
    {
        var opts = new AiOptions { Provider = AiProvider.Anthropic, Anthropic = { Model = "claude-x" } };
        Action act = () => AiClientFactory.CreateStandardChatClient(opts);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Anthropic*");
    }

    [Fact]
    public void OpenAI_WithKey_BuildsClient()
    {
        var opts = new AiOptions
        {
            Provider = AiProvider.OpenAI,
            OpenAI = { ApiKey = "sk-stub", Model = "gpt-x" }
        };
        var client = AiClientFactory.CreateStandardChatClient(opts);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Reasoning_OpenAI_NoKey_Throws()
    {
        var opts = new AiOptions { Provider = AiProvider.OpenAI, OpenAI = { Model = "gpt-x" } };
        Action act = () => AiClientFactory.CreateReasoningChatClient(opts);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reasoning_OpenAI_WithKey_UsesReasoningModelWhenSet()
    {
        var opts = new AiOptions
        {
            Provider = AiProvider.OpenAI,
            OpenAI = { ApiKey = "sk-stub", Model = "gpt-x", ReasoningModel = "o1-mini", ReasoningEffort = "medium" }
        };
        var client = AiClientFactory.CreateReasoningChatClient(opts);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Reasoning_Azure_NoEndpoint_Throws()
    {
        var opts = new AiOptions { Provider = AiProvider.AzureOpenAI, AzureOpenAI = { Deployment = "d" } };
        Action act = () => AiClientFactory.CreateReasoningChatClient(opts);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Reasoning_Anthropic_WithKey_BuildsClient()
    {
        var opts = new AiOptions
        {
            Provider = AiProvider.Anthropic,
            Anthropic = { ApiKey = "stub", Model = "claude-x", ReasoningModel = "claude-x", MaxTokens = 1024 }
        };
        var client = AiClientFactory.CreateReasoningChatClient(opts);
        client.Should().NotBeNull();
    }
}
