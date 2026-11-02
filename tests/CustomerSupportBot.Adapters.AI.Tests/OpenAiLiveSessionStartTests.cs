// session.start: ses ve arka plan talimatları ayrıdır, tool'lar arka planda durur, sesli tur oturum kaydı tutulmaz.

using System.Text.Json;
using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class OpenAiLiveSessionStartTests
{
    private static OpenAiLiveClientAdapter Build(LiveVoiceOptions? options = null)
    {
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(OpenAiLiveClientAdapter.VoicePromptKey).Returns("SES PROMPTU\n");
        prompts.Get(OpenAiLiveClientAdapter.BackendPromptKey).Returns("ARKA PLAN PROMPTU\n");
        return new OpenAiLiveClientAdapter(
            Options.Create(new AiOptions()), Options.Create(options ?? new LiveVoiceOptions()), new RealtimeFunctionTools(), prompts,
            NullLogger<OpenAiLiveClientAdapter>.Instance);
    }

    private static JsonElement Session(OpenAiLiveClientAdapter adapter, string? context = null, params ConversationMessage[] history)
    {
        var json = adapter.BuildSessionStartJson(new LiveSessionSetup(context, history));
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("type").GetString().Should().Be("session.start");
        return root.GetProperty("session");
    }

    [Fact]
    public void AudioAndModel_ComeFromTheOptions()
    {
        var s = Session(Build(new LiveVoiceOptions { Model = "gpt-live-9", Voice = "cedar" }));

        s.GetProperty("model").GetString().Should().Be("gpt-live-9");
        s.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString().Should().Be("cedar");
        var format = s.GetProperty("audio").GetProperty("format");
        format.GetProperty("type").GetString().Should().Be("audio/pcm");
        format.GetProperty("rate").GetInt32().Should().Be(24000);
    }

    [Fact]
    public void Recording_IsNeverStored()
    {
        Session(Build()).GetProperty("store").GetBoolean().Should().BeFalse("ses kaydı 30 gün sağlayıcıda kalırdı");
    }

    [Fact]
    public void Prompts_AreSeparate_AndBothGetTheSessionContext()
    {
        var s = Session(Build(), "Müşteri: Ayşe Yılmaz");

        var voice = s.GetProperty("instructions").GetString();
        var backend = s.GetProperty("delegation").GetProperty("responses").GetProperty("instructions").GetString();
        voice.Should().StartWith("SES PROMPTU").And.EndWith("OTURUM BİLGİSİ:\nMüşteri: Ayşe Yılmaz").And.NotContain("ARKA PLAN");
        backend.Should().StartWith("ARKA PLAN PROMPTU").And.EndWith("OTURUM BİLGİSİ:\nMüşteri: Ayşe Yılmaz");
    }

    [Fact]
    public void Tools_LiveInTheBackend_OneCallAtATime()
    {
        var s = Session(Build(new LiveVoiceOptions { BackendModel = "gpt-6-luna", BackendReasoningEffort = "low" }));

        s.TryGetProperty("tools", out _).Should().BeFalse("ses modeli tool çağırmaz");
        var d = s.GetProperty("delegation");
        d.GetProperty("type").GetString().Should().Be("responses");
        var r = d.GetProperty("responses");
        r.GetProperty("model").GetString().Should().Be("gpt-6-luna");
        r.GetProperty("tool_choice").GetString().Should().Be("auto");
        r.GetProperty("parallel_tool_calls").GetBoolean().Should().BeFalse();
        r.GetProperty("reasoning").GetProperty("effort").GetString().Should().Be("low");
        r.TryGetProperty("service_tier", out _).Should().BeFalse();
        r.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString())
            .Should().BeEquivalentTo(new RealtimeFunctionTools().GetToolNames());
    }

    [Fact]
    public void OptionalBackendSettings_AreSentOnlyWhenSet()
    {
        var r = Session(Build(new LiveVoiceOptions { BackendReasoningEffort = "", BackendServiceTier = "priority" }))
            .GetProperty("delegation").GetProperty("responses");

        r.TryGetProperty("reasoning", out _).Should().BeFalse();
        r.GetProperty("service_tier").GetString().Should().Be("priority");
    }

    [Fact]
    public void History_IsSeededWithTheRolesTheApiExpects()
    {
        var s = Session(Build(), null,
            new ConversationMessage(ConversationRoles.User, "siparişim nerede"),
            new ConversationMessage(ConversationRoles.Assistant, "Kargoda."));

        var input = s.GetProperty("input").EnumerateArray().ToList();
        input.Should().HaveCount(2);
        input[0].GetProperty("role").GetString().Should().Be("user");
        input[0].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
        input[0].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("siparişim nerede");
        input[1].GetProperty("role").GetString().Should().Be("assistant");
        input[1].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
    }

    [Fact]
    public void History_KeepsOnlyTheNewestMessages()
    {
        var history = Enumerable.Range(1, 5)
            .Select(i => new ConversationMessage(ConversationRoles.User, $"mesaj-{i}")).ToArray();

        var input = Session(Build(new LiveVoiceOptions { HistoryMessages = 2 }), null, history).GetProperty("input");

        input.EnumerateArray().Select(m => m.GetProperty("content")[0].GetProperty("text").GetString())
            .Should().Equal("mesaj-4", "mesaj-5");
    }

    [Fact]
    public void History_SkipsEmptyAndPlaceholderMessages()
    {
        var s = Session(Build(), null,
            new ConversationMessage(ConversationRoles.User, "(sesli)"),
            new ConversationMessage(ConversationRoles.User, "  "),
            new ConversationMessage(ConversationRoles.Assistant, "Merhaba"));

        s.GetProperty("input").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void History_StaysWithinTheProviderTokenLimit()
    {
        var huge = new string('x', OpenAiLiveClientAdapter.MaxHistoryChars);
        var s = Session(Build(), null,
            new ConversationMessage(ConversationRoles.User, huge),
            new ConversationMessage(ConversationRoles.User, "son mesaj"));

        s.GetProperty("input").EnumerateArray().Select(m => m.GetProperty("content")[0].GetProperty("text").GetString())
            .Should().Equal("son mesaj");
    }

    [Fact]
    public void Instruction_CarriesAnExplicitNullDelegation()
    {
        var root = JsonDocument.Parse(OpenAiLiveClientAdapter.BuildInstructionJson("Sus.")).RootElement;

        root.GetProperty("type").GetString().Should().Be("session.instructions.append");
        root.GetProperty("delegation_id").ValueKind.Should().Be(JsonValueKind.Null, "alan zorunlu; atlanırsa komut reddedilir");
        root.GetProperty("content").GetString().Should().Be("Sus.");
    }
}
