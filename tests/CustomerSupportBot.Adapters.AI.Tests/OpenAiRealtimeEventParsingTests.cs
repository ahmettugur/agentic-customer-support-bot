// OpenAI Realtime olaylarının port modeline eşlenmesi.
//
// Native sesli modda tur yarılarını eşleştirmenin tek güvenilir anahtarı kullanıcı ses öğesinin
// kimliğidir (item_id): transkript yanıt olaylarından önce de sonra da gelebilir. Adaptör bu
// kimliği taşımazsa servis yine "son gelen transkript"e düşer.

using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Application.Ports.Outbound.AI;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class OpenAiRealtimeEventParsingTests
{
    [Fact]
    public void InputAudioCommitted_CarriesItemId()
    {
        var evt = OpenAiRealtimeClientAdapter.ParseEvent(
            """{"type":"input_audio_buffer.committed","event_id":"e1","previous_item_id":null,"item_id":"item_U1"}""");

        evt!.EventType.Should().Be(RealtimeServerEventType.InputAudioCommitted);
        evt.ItemId.Should().Be("item_U1");
    }

    [Fact]
    public void TranscriptionCompleted_CarriesItemIdAndTranscript()
    {
        var evt = OpenAiRealtimeClientAdapter.ParseEvent(
            """{"type":"conversation.item.input_audio_transcription.completed","item_id":"item_U1","content_index":0,"transcript":"siparişim nerede"}""");

        evt!.EventType.Should().Be(RealtimeServerEventType.InputTranscriptCompleted);
        evt.ItemId.Should().Be("item_U1");
        evt.Transcript.Should().Be("siparişim nerede");
    }

    [Fact]
    public void TranscriptionFailed_IsSurfaced_WithItemId()
    {
        var evt = OpenAiRealtimeClientAdapter.ParseEvent(
            """{"type":"conversation.item.input_audio_transcription.failed","item_id":"item_U1","content_index":0,"error":{"type":"transcription_error","message":"audio unintelligible"}}""");

        evt!.EventType.Should().Be(RealtimeServerEventType.InputTranscriptFailed);
        evt.ItemId.Should().Be("item_U1");
        evt.ErrorMessage.Should().Be("audio unintelligible");
    }
}
