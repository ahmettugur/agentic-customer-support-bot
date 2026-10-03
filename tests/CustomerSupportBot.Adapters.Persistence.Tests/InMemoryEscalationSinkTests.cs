// Tests/Services/InMemoryEscalationSinkTests.cs

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryEscalationSinkTests
{
    private readonly InMemoryEscalationSink _sink = new(NullLogger<InMemoryEscalationSink>.Instance);

    private static EscalationRequest NewReq(string id = "e1") => new()
    {
        Id = id,
        // Kayıt başına ayrı session: aynı session + ajan için ikinci açık kayıt artık
        // oluşturulmuyor (dedup) — o davranış aşağıda ayrıca test ediliyor.
        SessionId = $"s-{id}",
        AgentName = "ComplaintAgent",
        Reason = "needs human",
        UserQuery = "şikayet",
        Status = EscalationStatus.Open,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Create_StoresAndFiresEvent()
    {
        EscalationRequest? captured = null;
        _sink.RequestCreated += (_, r) => captured = r;
        var req = await _sink.CreateAsync(NewReq());
        captured.Should().BeSameAs(req);
        _sink.Get("e1").Should().BeSameAs(req);
    }

    [Fact]
    public async Task GetOpen_FiltersOpenAndAcknowledged()
    {
        var open = await _sink.CreateAsync(NewReq("o1"));
        var ack = await _sink.CreateAsync(NewReq("o2"));
        ack.Status = EscalationStatus.Acknowledged;
        var resolved = await _sink.CreateAsync(NewReq("o3"));
        resolved.Status = EscalationStatus.Resolved;

        _sink.GetOpen().Should().HaveCount(2);
    }

    [Fact]
    public async Task Decide_AcknowledgeOpen_Transitions()
    {
        await _sink.CreateAsync(NewReq("e1"));
        (await _sink.DecideAsync("e1", "acknowledge", "agent42")).Should().BeTrue();
        _sink.Get("e1")!.Status.Should().Be(EscalationStatus.Acknowledged);
    }

    [Fact]
    public async Task Decide_Resolve_TransitionsAndCapturesResolution()
    {
        await _sink.CreateAsync(NewReq("e1"));
        (await _sink.DecideAsync("e1", "resolve", "agent42", "fixed")).Should().BeTrue();
        var req = _sink.Get("e1")!;
        req.Status.Should().Be(EscalationStatus.Resolved);
        req.Resolution.Should().Be("fixed");
    }

    [Fact]
    public async Task Decide_Dismiss_Transitions()
    {
        await _sink.CreateAsync(NewReq("e1"));
        (await _sink.DecideAsync("e1", "dismiss", "agent42", null)).Should().BeTrue();
        _sink.Get("e1")!.Status.Should().Be(EscalationStatus.Dismissed);
    }

    [Fact]
    public async Task Decide_UnknownAction_False()
    {
        await _sink.CreateAsync(NewReq("e1"));
        (await _sink.DecideAsync("e1", "weird", null, null)).Should().BeFalse();
    }

    [Fact]
    public async Task Decide_UnknownId_False()
    {
        (await _sink.DecideAsync("nope", "resolve", null, null)).Should().BeFalse();
    }

    [Fact]
    public async Task Decide_OnTerminalState_False()
    {
        await _sink.CreateAsync(NewReq("e1"));
        await _sink.DecideAsync("e1", "resolve", null, "x");
        (await _sink.DecideAsync("e1", "acknowledge", null, null)).Should().BeFalse();
    }

    [Fact]
    public async Task GetRecent_ReturnsByDescCreated()
    {
        await _sink.CreateAsync(NewReq("a"));
        Thread.Sleep(5);
        await _sink.CreateAsync(NewReq("b"));
        var recent = _sink.GetRecent(10);
        recent[0].Id.Should().Be("b");
        recent[1].Id.Should().Be("a");
    }

    [Fact]
    public async Task Create_SameSessionAndAgentWhileOpen_ReturnsExistingAndFiresNoEvent()
    {
        var first = await _sink.CreateAsync(NewReq("e1"));
        var created = 0;
        _sink.RequestCreated += (_, _) => created++;

        var duplicate = NewReq("e2");
        duplicate.SessionId = first.SessionId;
        var stored = await _sink.CreateAsync(duplicate);

        stored.Should().BeSameAs(first);
        _sink.Get("e2").Should().BeNull();
        _sink.GetOpen().Should().ContainSingle();
        created.Should().Be(0);
    }

    [Fact]
    public async Task Create_SameSessionDifferentAgent_CreatesBoth()
    {
        var first = await _sink.CreateAsync(NewReq("e1"));
        var other = NewReq("e2");
        other.SessionId = first.SessionId;
        other.AgentName = "OrderAgent";

        (await _sink.CreateAsync(other)).Id.Should().Be("e2");
        _sink.GetOpen().Should().HaveCount(2);
    }

    [Fact]
    public async Task Create_AfterPreviousClosed_CreatesNew()
    {
        var first = await _sink.CreateAsync(NewReq("e1"));
        await _sink.DecideAsync("e1", "resolve", "agent42", "ok");

        var again = NewReq("e2");
        again.SessionId = first.SessionId;

        (await _sink.CreateAsync(again)).Id.Should().Be("e2");
    }

    [Fact]
    public async Task Create_ConcurrentSameSessionAndAgent_OnlyOneStored()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            var r = NewReq($"c{i}");
            r.SessionId = "shared";
            return _sink.CreateAsync(r);
        })));

        results.Select(r => r.Id).Distinct().Should().ContainSingle();
        _sink.GetOpen().Should().ContainSingle();
    }

    [Fact]
    public async Task Decide_ConcurrentConflictingDecisions_OnlyOneWins()
    {
        await _sink.CreateAsync(NewReq("e1"));

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
            _sink.DecideAsync("e1", i % 2 == 0 ? "resolve" : "dismiss", $"admin{i}", $"r{i}"))));

        outcomes.Count(o => o).Should().Be(1, "terminal geçişe yalnızca bir karar ulaşabilir");
    }
}
