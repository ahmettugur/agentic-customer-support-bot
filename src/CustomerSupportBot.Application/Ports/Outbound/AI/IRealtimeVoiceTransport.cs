namespace CustomerSupportBot.Application.Ports.Outbound.AI;

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

    /// <summary>
    /// Sesli oturum konfigürasyonunu gönderir (model kendi cevaplar + tool'lar açık).
    /// </summary>
    /// <param name="sessionContext">
    /// Oturuma özel ek system talimatı — login'li müşterinin adı ve bugünün tarihi
    /// (bkz. <c>CustomerIdentityHintBuilder</c>). Sabit talimatların SONUNA eklenir; null/boşsa
    /// yalnızca sabit talimatlar gönderilir. Yazılı kanal bu bilgiyi mesaj listesine system
    /// mesajı olarak koyuyor — sesli kanalda mesaj listesi olmadığı için buradan geçirilir.
    /// </param>
    Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct);

    /// <summary>Browser'dan gelen PCM16 audio chunk'ını base64 encode edip provider'a iletir.</summary>
    Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct);

    /// <summary>Devam eden yanıtı iptal eder.</summary>
    Task SendInterruptAsync(CancellationToken ct);

    /// <summary>
    /// <c>true</c>: oturum yanıtı kendiliğinden başlatmaz; uygulama transkripti girdi korumasından geçirip
    /// <see cref="RequestResponseAsync"/> çağırır (<c>Realtime:WaitForInputGuard</c>).
    /// </summary>
    bool WaitsForInputGuard { get; }

    /// <summary>Model yanıtını başlatır (yalnızca <see cref="WaitsForInputGuard"/> modunda kullanılır).</summary>
    Task RequestResponseAsync(CancellationToken ct);

    /// <summary>WS bağlantısını kapatır.</summary>
    Task CloseAsync(string reason, CancellationToken ct);

    /// <summary>
    /// Function call sonuçlarını provider'a iletir.
    /// <paramref name="triggerNextResponse"/> true ise ardından response.create gönderir.
    /// </summary>
    Task SendToolResultsAsync(
        IReadOnlyList<RealtimeToolResult> results,
        bool triggerNextResponse,
        CancellationToken ct);

    /// <summary>Provider'dan gelen event stream'ini okur. Bağlantı kapanana kadar yield eder.</summary>
    IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(CancellationToken ct);
}
