using System.Text;
using CustomerSupportBot.Api.Infrastructure;
using CustomerSupportBot.Domain.Model;
using Microsoft.AspNetCore.Http;

namespace CustomerSupportBot.Api.Tests.Infrastructure;

public class SseWriterTests
{
    private static (DefaultHttpContext ctx, MemoryStream body) BuildResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    [Fact]
    public void WriteHeaders_SetsSseHeaders()
    {
        var (ctx, _) = BuildResponse();
        SseWriter.WriteHeaders(ctx.Response);
        ctx.Response.Headers["Content-Type"].ToString().Should().Be("text/event-stream");
        ctx.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache, no-transform");
        ctx.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
        ctx.Response.Headers["Connection"].ToString().Should().Be("keep-alive");
    }

    [Fact]
    public async Task WriteEvent_WritesSseFormattedPayload()
    {
        var (ctx, body) = BuildResponse();
        await SseWriter.WriteEventAsync(ctx.Response, "test", new { foo = "bar" }, TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().StartWith("event: test\n");
        text.Should().Contain("\"foo\":\"bar\"");
        text.Should().EndWith("\n\n");
    }

    [Fact]
    public async Task WriteEvent_NullData_WritesEmptyJson()
    {
        var (ctx, body) = BuildResponse();
        await SseWriter.WriteEventAsync(ctx.Response, "ping", null, TestContext.Current.CancellationToken);
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().Contain("data: {}");
    }

    [Fact]
    public async Task WriteEvent_CancelledToken_NoOp()
    {
        var (ctx, body) = BuildResponse();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await SseWriter.WriteEventAsync(ctx.Response, "x", new { }, cts.Token);
        body.Length.Should().Be(0);
    }

    [Fact]
    public void GetTextFromAnon_ReturnsTextProperty()
    {
        var data = new { text = "merhaba" };
        SseWriter.GetTextFromAnon(data).Should().Be("merhaba");
    }

    [Fact]
    public void GetTextFromAnon_NoTextProperty_ReturnsEmpty()
    {
        var data = new { other = "x" };
        SseWriter.GetTextFromAnon(data).Should().Be("");
    }
}

public class SseForwarderTests
{
    private static (DefaultHttpContext ctx, MemoryStream body) BuildResponse()
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        return (ctx, body);
    }

    [Fact]
    public void Constructor_NullResponse_Throws()
    {
        Action act = () => _ = new SseForwarder(null!, default);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_WritesEvent()
    {
        var (ctx, body) = BuildResponse();
        using var fwd = new SseForwarder(ctx.Response, default);
        await fwd.WriteAsync("evt", new { x = 1 });
        Encoding.UTF8.GetString(body.ToArray()).Should().Contain("event: evt");
    }

    [Fact]
    public async Task WriteAsync_AfterDispose_NoOp()
    {
        var (ctx, body) = BuildResponse();
        var fwd = new SseForwarder(ctx.Response, default);
        fwd.Dispose();
        await fwd.WriteAsync("evt", new { });
        body.Length.Should().Be(0);
    }

    [Fact]
    public async Task WriteAsync_CancelledToken_NoOp()
    {
        var (ctx, body) = BuildResponse();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var fwd = new SseForwarder(ctx.Response, cts.Token);
        await fwd.WriteAsync("evt", new { });
        body.Length.Should().Be(0);
    }

    [Fact]
    public async Task WriteSession_WritesSessionEvent()
    {
        var (ctx, body) = BuildResponse();
        using var fwd = new SseForwarder(ctx.Response, default);
        await fwd.WriteSessionAsync("s1");
        Encoding.UTF8.GetString(body.ToArray()).Should().Contain(StreamEventTypes.Session);
    }

    [Fact]
    public async Task WriteDone_WritesDoneEvent()
    {
        var (ctx, body) = BuildResponse();
        using var fwd = new SseForwarder(ctx.Response, default);
        await fwd.WriteDoneAsync("s1");
        Encoding.UTF8.GetString(body.ToArray()).Should().Contain(StreamEventTypes.Done);
    }

    [Fact]
    public async Task WriteError_WritesErrorEvent()
    {
        var (ctx, body) = BuildResponse();
        using var fwd = new SseForwarder(ctx.Response, default);
        await fwd.WriteErrorAsync("hata");
        var text = Encoding.UTF8.GetString(body.ToArray());
        text.Should().Contain(StreamEventTypes.Error);
        text.Should().Contain("hata");
    }

    [Fact]
    public void Dispose_Idempotent()
    {
        var (ctx, _) = BuildResponse();
        var fwd = new SseForwarder(ctx.Response, default);
        fwd.Dispose();
        fwd.Dispose();
    }
}
