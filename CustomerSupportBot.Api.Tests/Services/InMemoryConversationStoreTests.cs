using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Tests.Services;

public class InMemoryConversationStoreTests
{
    private readonly InMemoryConversationStore _store = new();

    [Fact]
    public void GetHistory_NewSession_Empty() =>
        _store.GetHistory("s1").Should().BeEmpty();

    [Fact]
    public void AddExchange_AddsTwoMessages()
    {
        _store.AddExchange("s1", "soru", "cevap");
        var h = _store.GetHistory("s1");
        h.Should().HaveCount(2);
        h[0].Role.Should().Be(ChatRole.User);
        h[0].Text.Should().Be("soru");
        h[1].Role.Should().Be(ChatRole.Assistant);
        h[1].Text.Should().Be("cevap");
    }

    [Fact]
    public void AddExchange_LongQuery_TitleTruncated()
    {
        var longQ = new string('a', 100);
        _store.AddExchange("s1", longQ, "cevap");
        var meta = _store.GetAllSessions()[0];
        meta.Title.Should().EndWith("...");
        meta.Title.Length.Should().Be(53);
    }

    [Fact]
    public void AddExchange_ShortQuery_TitleAsIs()
    {
        _store.AddExchange("s1", "kısa", "cevap");
        _store.GetAllSessions()[0].Title.Should().Be("kısa");
    }

    [Fact]
    public void ClearSession_RemovesData()
    {
        _store.AddExchange("s1", "q", "r");
        _store.ClearSession("s1");
        _store.GetHistory("s1").Should().BeEmpty();
        _store.GetAllSessions().Should().BeEmpty();
    }

    [Fact]
    public void AppendAssistantMessage_EmptySession_AddsMessage()
    {
        _store.AppendAssistantMessage("s1", "selam");
        _store.GetHistory("s1").Should().ContainSingle()
            .Which.Text.Should().Be("selam");
    }

    [Fact]
    public void AppendAssistantMessage_BlankIgnored()
    {
        _store.AppendAssistantMessage("s1", "   ");
        _store.GetHistory("s1").Should().BeEmpty();
    }

    [Fact]
    public void AppendAssistantMessage_LastEmptyAssistant_FillsPlaceholder()
    {
        var sessId = "s1";
        // Direkt history listesine boş asistan mesajı ekleyemiyoruz; AddExchange ile dolaşmadan
        // önce bir exchange ekleyelim sonra son mesajı boş yapamayız. Bu testi atlıyoruz.
        _store.AddExchange(sessId, "soru", "");  // Asistan mesajı boş
        _store.AppendAssistantMessage(sessId, "doldurulmuş");
        var h = _store.GetHistory(sessId);
        h.Should().HaveCount(2);
        h[1].Text.Should().Be("doldurulmuş");
    }

    [Fact]
    public void AppendAssistantMessage_LastFilledAssistant_AppendsNew()
    {
        _store.AddExchange("s1", "q", "r");
        _store.AppendAssistantMessage("s1", "ek");
        var h = _store.GetHistory("s1");
        h.Should().HaveCount(3);
        h[2].Text.Should().Be("ek");
    }

    [Fact]
    public void GetAllSessions_OrderedByLastActivityDesc()
    {
        _store.AddExchange("s1", "ilk", "");
        Thread.Sleep(20);
        _store.AddExchange("s2", "ikinci", "");
        var sessions = _store.GetAllSessions();
        sessions[0].SessionId.Should().Be("s2");
        sessions[1].SessionId.Should().Be("s1");
    }
}

public class ApprovalContextAccessorTests
{
    [Fact]
    public void Context_BeforeScope_Null()
    {
        var acc = new ApprovalContextAccessor();
        acc.Context.Should().BeNull();
    }

    [Fact]
    public void SetScope_PopulatesContext()
    {
        var acc = new ApprovalContextAccessor();
        using var s = acc.SetScope("sid", "tid", "soru");
        acc.Context.Should().NotBeNull();
        acc.Context!.SessionId.Should().Be("sid");
        acc.Context.TraceId.Should().Be("tid");
        acc.Context.UserQuery.Should().Be("soru");
    }

    [Fact]
    public void SetScope_Disposed_ContextRestored()
    {
        var acc = new ApprovalContextAccessor();
        using (acc.SetScope("a", null, null))
        {
            acc.Context!.SessionId.Should().Be("a");
        }
        acc.Context.Should().BeNull();
    }

    [Fact]
    public void SetScope_Nested_RestoresPreviousOnInnerDispose()
    {
        var acc = new ApprovalContextAccessor();
        using var outer = acc.SetScope("outer", null, null);
        using (acc.SetScope("inner", null, null))
        {
            acc.Context!.SessionId.Should().Be("inner");
        }
        acc.Context!.SessionId.Should().Be("outer");
    }
}

public class ContextPipelineTests
{
    private sealed class StubProvider(string name, int order, string? output) : IContextProvider
    {
        public string Name => name;
        public int Order => order;
        public Task<string?> GetContextAsync(AgentSession session) => Task.FromResult(output);
    }

    private sealed class ThrowingProvider : IContextProvider
    {
        public string Name => "Throws";
        public int Order => 100;
        public Task<string?> GetContextAsync(AgentSession session) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task BuildContext_NoProviders_ReturnsEmpty()
    {
        var p = new ContextPipeline(Enumerable.Empty<IContextProvider>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextPipeline>.Instance);
        var result = await p.BuildContextAsync(new AgentSession { SessionId = "s1" });
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildContext_OrderedByOrder_JoinedWithBlankLine()
    {
        var providers = new IContextProvider[]
        {
            new StubProvider("B", 20, "second"),
            new StubProvider("A", 10, "first")
        };
        var p = new ContextPipeline(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextPipeline>.Instance);
        var result = await p.BuildContextAsync(new AgentSession { SessionId = "s1" });
        result.Should().Be("first\n\nsecond");
    }

    [Fact]
    public async Task BuildContext_ThrowingProvider_Skipped()
    {
        var providers = new IContextProvider[]
        {
            new ThrowingProvider(),
            new StubProvider("Ok", 1, "ok")
        };
        var p = new ContextPipeline(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextPipeline>.Instance);
        var result = await p.BuildContextAsync(new AgentSession { SessionId = "s1" });
        result.Should().Be("ok");
    }

    [Fact]
    public async Task BuildContext_NullOutput_Skipped()
    {
        var providers = new IContextProvider[]
        {
            new StubProvider("Null", 1, null),
            new StubProvider("Ok", 2, "x")
        };
        var p = new ContextPipeline(providers,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContextPipeline>.Instance);
        var result = await p.BuildContextAsync(new AgentSession { SessionId = "s1" });
        result.Should().Be("x");
    }
}
