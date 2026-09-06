using System.Net;
using System.Net.Http.Headers;
using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Api.IntegrationTests;

public class TraceClientRateLimitTests
{
    [Theory]
    [InlineData("delta", 30)]
    [InlineData("date", 45)]
    [InlineData("missing", 60)]
    public async Task TraceReads_PauseAfter429_AndResumeAfterRetryAfter(string kind, int seconds)
    {
        var clock = new ManualClock();
        using var handler = new StubHandler(call =>
        {
            if (call != 1) return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (kind == "delta") response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            if (kind == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(seconds));
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var api = new TracesApiService(http, clock);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetSessionsAsync());
        error.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        clock.Advance(TimeSpan.FromSeconds(seconds - 1));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetSessionsAsync());
        await api.GetTraceAsync("trace");
        await api.GetBySessionAsync("session");
        await api.GetApprovalsBySessionAsync("session");
        handler.Calls.Should().Be(1, "all trace reads share the cooldown without sending more HTTP requests");

        clock.Advance(TimeSpan.FromSeconds(1));
        (await api.GetSessionsAsync()).Should().BeEmpty();
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task FailedSessionRefresh_IsNotReportedAsAnEmptySuccessfulList()
    {
        using var handler = new StubHandler(call => call == 1
            ? new(HttpStatusCode.OK) { Content = new StringContent("[{\"sessionId\":\"s1\",\"traceCount\":1}]") }
            : new(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var api = new TracesApiService(http);
        var previous = await api.GetSessionsAsync();
        previous.Should().ContainSingle().Which.SessionId.Should().Be("s1");
        await Assert.ThrowsAsync<HttpRequestException>(() => api.GetSessionsAsync());
        previous.Should().ContainSingle();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class StubHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(++Calls));
    }
}
