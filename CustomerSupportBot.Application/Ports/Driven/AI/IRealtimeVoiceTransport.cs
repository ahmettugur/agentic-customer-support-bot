// Application/Ports/Driven/AI/IRealtimeVoiceTransport.cs
// SECONDARY PORT — Realtime sesli API WebSocket transport soyutlaması.
// ClientWebSocket, PCM16 base64 encode/decode ve provider-specific event JSON şeması
// bu port'un arkasında gizlenir.
// Implementasyon: Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.

namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// Realtime sesli API ile WebSocket transport için secondary (driven) port.
/// Application katmanı bu port'u çağırır; adaptör provider-specific protokolü bilir.
/// Vendor-agnostik: OpenAI, Google veya başka bir sağlayıcı ile değiştirilebilir.
/// </summary>
public interface IRealtimeVoiceTransport : IAsyncDisposable
{
    bool IsEnabled { get; }
    string ModelName { get; }
    string Voice { get; }

    /// <summary>Native modda kullanılan tool adları — frontend'e bilgi vermek için.</summary>
    IReadOnlyList<string> NativeToolNames { get; }

    /// <summary>Realtime WS'e bağlanır. Başarısız olursa false döner.</summary>
    Task<bool> TryConnectAsync(CancellationToken ct);

    /// <summary>Bridge modu session konfigürasyonunu gönderir (model otomatik yanıt vermez).</summary>
    Task ConfigureBridgeSessionAsync(CancellationToken ct);

    /// <summary>Native modu session konfigürasyonunu gönderir (model kendi cevaplar + tool'lar açık).</summary>
    Task ConfigureNativeSessionAsync(CancellationToken ct);

    /// <summary>Browser'dan gelen PCM16 audio chunk'ını base64 encode edip provider'a iletir.</summary>
    Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct);

    /// <summary>Devam eden yanıtı iptal eder.</summary>
    Task SendInterruptAsync(CancellationToken ct);

    /// <summary>WS bağlantısını kapatır.</summary>
    Task CloseAsync(string reason, CancellationToken ct);

    /// <summary>Bridge modu: asistan cevap metnini provider'a seslendirme için gönderir.</summary>
    Task SpeakTextAsync(string text, string speakInstructions, CancellationToken ct);

    /// <summary>
    /// Native modu: function call sonuçlarını provider'a iletir.
    /// <paramref name="triggerNextResponse"/> true ise ardından response.create gönderir.
    /// </summary>
    Task SendToolResultsAsync(
        IReadOnlyList<RealtimeToolResult> results,
        bool triggerNextResponse,
        CancellationToken ct);

    /// <summary>Provider'dan gelen event stream'ini okur. Bağlantı kapanana kadar yield eder.</summary>
    IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct);
}
