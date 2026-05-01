// Tests/Services/InMemoryReasoningTraceStoreTests.cs
using CustomerSupportBot.Services;

namespace CustomerSupportBot.Tests.Services;

public class InMemoryReasoningTraceStoreTests
{
    [Fact]
    public void StartTrace_AssignsTraceId()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = store.StartTrace("s1", "query");
        trace.TraceId.Should().NotBeNullOrEmpty();
        trace.SessionId.Should().Be("s1");
        trace.UserQuery.Should().Be("query");
    }

    [Fact]
    public void Get_KnownTrace_ReturnsTrace()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = store.StartTrace("s1", "q");
        store.Get(trace.TraceId).Should().BeSameAs(trace);
    }

    [Fact]
    public void Get_Unknown_ReturnsNull()
    {
        var store = new InMemoryReasoningTraceStore();
        store.Get("nope").Should().BeNull();
    }

    [Fact]
    public void Complete_SetsFinalResponseAndTermination()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = store.StartTrace("s1", "q");
        store.Complete(trace.TraceId, "completed", "resp", null);

        trace.CompletedAt.Should().NotBeNull();
        trace.TerminationReason.Should().Be("completed");
        trace.FinalResponse.Should().Be("resp");
    }

    [Fact]
    public void Complete_LongResponse_Truncated()
    {
        var store = new InMemoryReasoningTraceStore();
        var trace = store.StartTrace("s1", "q");
        var huge = new string('x', 5000);
        store.Complete(trace.TraceId, null, huge, null);
        trace.FinalResponse!.Length.Should().BeLessThanOrEqualTo(2001 + 1);
        trace.FinalResponse.Should().EndWith("…");
    }

    [Fact]
    public void Complete_UnknownTrace_NoOp()
    {
        var store = new InMemoryReasoningTraceStore();
        var act = () => store.Complete("nope", "x", "y", "z");
        act.Should().NotThrow();
    }

    [Fact]
    public void GetRecent_ReturnsByDescStart()
    {
        var store = new InMemoryReasoningTraceStore();
        var t1 = store.StartTrace("s1", "q1");
        Thread.Sleep(10);
        var t2 = store.StartTrace("s2", "q2");
        var recent = store.GetRecent(10);
        recent[0].TraceId.Should().Be(t2.TraceId);
        recent[1].TraceId.Should().Be(t1.TraceId);
    }

    [Fact]
    public void GetBySession_FiltersById()
    {
        var store = new InMemoryReasoningTraceStore();
        store.StartTrace("s1", "q1");
        store.StartTrace("s2", "q2");
        store.StartTrace("s1", "q3");

        var sessionTraces = store.GetBySession("s1");
        sessionTraces.Should().HaveCount(2);
        sessionTraces.Should().OnlyContain(t => t.SessionId == "s1");
    }

    [Fact]
    public void StartTrace_CapacityExceeded_DropsOldest()
    {
        var store = new InMemoryReasoningTraceStore(maxCapacity: 3);
        var t1 = store.StartTrace("s1", "q1");
        store.StartTrace("s2", "q2");
        store.StartTrace("s3", "q3");
        store.StartTrace("s4", "q4");
        store.Get(t1.TraceId).Should().BeNull();
    }
}
