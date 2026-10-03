// Sesli oturum talimatının kaynağı: Prompts/services/realtime-voice.md (eskiden koda gömülüydü).

using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class OpenAiRealtimeInstructionsTests
{
    private static OpenAiRealtimeClientAdapter Build(string promptText)
    {
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(OpenAiRealtimeClientAdapter.InstructionsPromptKey).Returns(promptText);
        return new OpenAiRealtimeClientAdapter(
            Options.Create(new AiOptions()), new RealtimeFunctionTools(), prompts,
            NullLogger<OpenAiRealtimeClientAdapter>.Instance);
    }

    [Fact]
    public void Instructions_ComeFromThePromptFile()
    {
        Build("PROMPT DOSYASI\n").BuildSessionInstructions(null).Should().Be("PROMPT DOSYASI");
    }

    [Fact]
    public void SessionContext_IsAppendedAfterThePrompt()
    {
        var text = Build("PROMPT").BuildSessionInstructions("Müşteri: Ayşe Yılmaz\nBugün: 3 Ekim 2026");

        text.Should().StartWith("PROMPT");
        text.Should().EndWith("OTURUM BİLGİSİ:\nMüşteri: Ayşe Yılmaz\nBugün: 3 Ekim 2026");
    }
}
