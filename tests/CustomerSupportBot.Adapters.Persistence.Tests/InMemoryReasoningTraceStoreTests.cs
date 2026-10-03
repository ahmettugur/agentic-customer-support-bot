// Tests/Services/InMemoryReasoningTraceStoreTests.cs

using CustomerSupportBot.Adapters.Persistence.InMemory;
namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryReasoningTraceStoreTests
{
    [Fact]
    public async Task StartTrace_AssignsTraceId()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = await store.StartTraceAsync("s1", "query");
        trace.TraceId.Should().NotBeNullOrEmpty();
        trace.SessionId.Should().Be("s1");
        trace.UserQuery.Should().Be("query");
    }

    [Fact]
    public async Task Get_KnownTrace_ReturnsTrace()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = await store.StartTraceAsync("s1", "q");
        store.Get(trace.TraceId).Should().BeSameAs(trace);
    }

    [Fact]
    public void Get_Unknown_ReturnsNull()
    {
        var store = new InMemoryReasoningTraceStore();
        store.Get("nope").Should().BeNull();
    }

    [Fact]
    public async Task Complete_SetsFinalResponseAndTermination()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = await store.StartTraceAsync("s1", "q");
        await store.CompleteAsync(trace.TraceId, "completed", "resp", null);

        trace.CompletedAt.Should().NotBeNull();
        trace.TerminationReason.Should().Be("completed");
        trace.FinalResponse.Should().Be("resp");
    }

    [Fact]
    public async Task Complete_LongResponse_Truncated()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = await store.StartTraceAsync("s1", "q");
        var huge = new string('x', 5000);
        await store.CompleteAsync(trace.TraceId, null, huge, null);
        trace.FinalResponse!.Length.Should().BeLessThanOrEqualTo(2001 + 1);
        trace.FinalResponse.Should().EndWith("…");
    }

    [Fact]
    public async Task Complete_UnknownTrace_NoOp()
    {
        var store = new InMemoryReasoningTraceStore();
        var act = () => store.CompleteAsync("nope", "x", "y", "z");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetRecent_ReturnsByDescStart()
    {
        var store = new InMemoryReasoningTraceStore();
        var t1 = await store.StartTraceAsync("s1", "q1");
        await Task.Delay(10, TestContext.Current.CancellationToken);
        var t2 = await store.StartTraceAsync("s2", "q2");
        var recent = store.GetRecent(10);
        recent[0].TraceId.Should().Be(t2.TraceId);
        recent[1].TraceId.Should().Be(t1.TraceId);
    }

    [Fact]
    public async Task GetBySession_FiltersById()
    {
        var store = new InMemoryReasoningTraceStore();
        await store.StartTraceAsync("s1", "q1");
        await store.StartTraceAsync("s2", "q2");
        await store.StartTraceAsync("s1", "q3");

        var sessionTraces = store.GetBySession("s1");
        sessionTraces.Should().HaveCount(2);
        sessionTraces.Should().OnlyContain(t => t.SessionId == "s1");
    }

    [Fact]
    public async Task StartTrace_CapacityExceeded_DropsOldest()
    {
        var store = new InMemoryReasoningTraceStore(maxCapacity: 3);
        var t1 = await store.StartTraceAsync("s1", "q1");
        await store.StartTraceAsync("s2", "q2");
        await store.StartTraceAsync("s3", "q3");
        await store.StartTraceAsync("s4", "q4");
        store.Get(t1.TraceId).Should().BeNull();
    }
}
