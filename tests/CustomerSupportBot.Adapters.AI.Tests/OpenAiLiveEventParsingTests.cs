// GPT-Live olaylarının uygulama olaylarına çevrilmesi. Örnekler gerçek API'den gözlenen biçimlerdir (gpt-live-1, 2026-11-01).

using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Outbound.AI;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class OpenAiLiveEventParsingTests
{
    private static string Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    private static LiveServerEvent Parse(string json) => OpenAiLiveClientAdapter.ParseEvent(json)!;

    [Fact]
    public void SessionStarted()
    {
        Parse("""{"type":"session.started","session":{"id":"live_1","expires_at":1793548821}}""")
            .EventType.Should().Be(LiveServerEventType.SessionStarted);
    }

    [Fact]
    public void AudioDelta_WithRealSpeech_IsMarkedAsSpeech()
    {
        var e = Parse($$"""{"type":"session.output_audio.delta","delta":"{{Pcm(0, 4768, -6432, 12)}}"}""");

        e.EventType.Should().Be(LiveServerEventType.AudioDelta);
        e.HasSpeech.Should().BeTrue();
        e.AudioDelta.Should().HaveCount(8);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)45)]
    [InlineData((short)-92)]
    public void AudioDelta_SilentOrNearSilentFrame_IsNotSpeech(short peak)
    {
        // GPT-Live konuşmadığı anlarda da 100 ms'lik paket gönderir: sıfır ya da çok kısık (ölçülen tepe 0-92).
        Parse($$"""{"type":"session.output_audio.delta","delta":"{{Pcm(0, peak, 0)}}"}""").HasSpeech.Should().BeFalse();
    }

    [Fact]
    public void InvalidAudio_IsIgnored()
    {
        OpenAiLiveClientAdapter.ParseEvent("""{"type":"session.output_audio.delta","delta":"@@@"}""").Should().BeNull();
    }

    [Fact]
    public void InputTranscriptDelta_KeepsTheFragmentExactly()
    {
        var e = Parse("""{"type":"session.input_transcript.delta","start_ms":600,"end_ms":800,"delta":" Mer"}""");

        e.EventType.Should().Be(LiveServerEventType.InputTranscriptDelta);
        e.TextDelta.Should().Be(" Mer");
    }

    [Fact]
    public void OutputTranscriptDelta()
    {
        var e = Parse("""{"type":"session.output_transcript.delta","start_ms":10000,"end_ms":10200,"delta":" Hemen"}""");

        e.EventType.Should().Be(LiveServerEventType.OutputTranscriptDelta);
        e.TextDelta.Should().Be(" Hemen");
    }

    [Fact]
    public void DelegationCreated()
    {
        var e = Parse("""
            {"type":"session.delegation.created","offset_ms":9800,
             "delegation":{"id":"item_1","type":"delegation","response_id":"resp_1","target":"responses"}}
            """);

        e.EventType.Should().Be(LiveServerEventType.DelegationCreated);
        e.DelegationId.Should().Be("item_1");
        e.ResponseId.Should().Be("resp_1");
    }

    [Fact]
    public void NestedFunctionCall_BecomesAToolCall_WithItsDelegation()
    {
        var e = Parse("""
            {"type":"response.event","delegation_id":"item_1","event":{"type":"response.output_item.done",
             "item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"order_status_tool","arguments":"{\"order_id\":\"1057\"}"}}}
            """);

        e.EventType.Should().Be(LiveServerEventType.ToolCallReady);
        e.DelegationId.Should().Be("item_1");
        e.ToolCallId.Should().Be("call_1");
        e.ToolName.Should().Be("order_status_tool");
        e.ToolArguments.Should().Contain("1057");
    }

    [Theory]
    [InlineData("""{"type":"response.event","delegation_id":"d","event":{"type":"response.output_item.done","item":{"type":"reasoning"}}}""")]
    [InlineData("""{"type":"response.event","delegation_id":"d","event":{"type":"response.output_item.done","item":{"type":"message"}}}""")]
    [InlineData("""{"type":"response.event","delegation_id":"d","event":{"type":"response.output_text.delta","delta":"x"}}""")]
    [InlineData("""{"type":"response.event","delegation_id":"d","event":{"type":"response.created","response":{"id":"r"}}}""")]
    [InlineData("""{"type":"session.updated","session":{}}""")]
    [InlineData("""{"type":"future.event"}""")]
    [InlineData("""bozuk json""")]
    public void OtherEvents_AreIgnored(string json)
    {
        OpenAiLiveClientAdapter.ParseEvent(json).Should().BeNull();
    }

    [Fact]
    public void NestedCompletion_CarriesUsageAndModel()
    {
        var e = Parse("""
            {"type":"response.event","delegation_id":"item_1","event":{"type":"response.completed","response":{"id":"resp_1","status":"completed",
             "model":"gpt-6-luna","output":[],"usage":{"input_tokens":1085,"output_tokens":27,"total_tokens":1112}}}}
            """);

        e.EventType.Should().Be(LiveServerEventType.BackendResponseCompleted);
        e.DelegationId.Should().Be("item_1");
        e.ResponseId.Should().Be("resp_1");
        e.BackendModel.Should().Be("gpt-6-luna");
        e.InputTokens.Should().Be(1085);
        e.OutputTokens.Should().Be(27);
    }

    [Theory]
    [InlineData("response.failed")]
    [InlineData("response.incomplete")]
    public void NestedFailure_IsReported(string type)
    {
        var e = Parse("""
            {"type":"response.event","delegation_id":"item_1","event":{"type":"TYPE","response":{"id":"resp_1","model":"gpt-6-luna",
             "error":{"code":"server_error","message":"olmadı"},"usage":{"input_tokens":10,"output_tokens":0}}}}
            """.Replace("TYPE", type));

        e.EventType.Should().Be(LiveServerEventType.BackendResponseFailed);
        e.DelegationId.Should().Be("item_1");
        e.ErrorMessage.Should().Be("olmadı");
        e.InputTokens.Should().Be(10);
    }

    [Fact]
    public void Usage_IsCumulativeSeconds()
    {
        var e = Parse("""{"type":"session.usage.updated","usage":{"seconds":32.5},"context_window":{"usage_ratio":0.01}}""");

        e.EventType.Should().Be(LiveServerEventType.UsageUpdated);
        e.UsageSeconds.Should().Be(32.5);
    }

    [Fact]
    public void SessionClosed_CarriesReasonAndFinalUsage()
    {
        var e = Parse("""{"type":"session.closed","reason":"close_requested","usage":{"seconds":32},"session":{"id":"live_1"}}""");

        e.EventType.Should().Be(LiveServerEventType.SessionClosed);
        e.CloseReason.Should().Be("close_requested");
        e.UsageSeconds.Should().Be(32);
    }

    [Fact]
    public void Error_CarriesCodeAndMessage()
    {
        var e = Parse("""{"type":"error","error":{"type":"invalid_request_error","code":"responses_validation_failed","message":"geçersiz"}}""");

        e.EventType.Should().Be(LiveServerEventType.Error);
        e.ErrorCode.Should().Be("responses_validation_failed");
        e.ErrorMessage.Should().Be("geçersiz");
    }
}
