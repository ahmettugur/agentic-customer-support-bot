namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <summary>
/// Tam çift yönlü ses motoru için secondary (driven) port. <see cref="IRealtimeVoiceTransport"/>'tan ayrıdır çünkü iş modeli farklıdır:
/// tur kavramı, yanıt iptali ve commit yoktur; ses iki yönde kesintisiz akar, tool'ları arka plan modeli çağırır.
/// </summary>
public interface ILiveVoiceTransport : IAsyncDisposable
{
    bool IsEnabled { get; }
    string ModelName { get; }
    string Voice { get; }

    /// <summary>Tarayıcıya bilgi için: arka plan modelinin kullanabildiği tool adları.</summary>
    IReadOnlyList<string> NativeToolNames { get; }

    /// <summary>Bağlantıyı açar. Başarısız olursa false döner.</summary>
    Task<bool> TryConnectAsync(CancellationToken ct);

    /// <summary>Oturumu başlatır. Hazır olduğunda <see cref="LiveServerEventType.SessionStarted"/> gelir; ses ondan önce gönderilmez.</summary>
    Task StartSessionAsync(LiveSessionSetup setup, CancellationToken ct);

    /// <summary>Mikrofon sesini iletir. Oturum zaman çizelgesi gelen sesle ilerler: sessizlikte de akmaya devam etmelidir.</summary>
    Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct);

    /// <summary>Tool sonuçlarını iletir; <paramref name="continueBackend"/> true ise arka plan modelini sürdürür.</summary>
    Task SendToolResultsAsync(IReadOnlyList<RealtimeToolResult> results, bool continueBackend, CancellationToken ct);

    /// <summary>Uygulamanın ses modeline talimatı. Metin uygulamanın yazdığı metindir; kullanıcı metni asla eklenmez.</summary>
    Task SendInstructionAsync(string text, CancellationToken ct);

    /// <summary>Kapanışı başlatır; son kullanım <see cref="LiveServerEventType.SessionClosed"/> ile gelir.</summary>
    Task RequestCloseAsync(CancellationToken ct);

    /// <summary>Sunucu olaylarını okur; bağlantı kapanana kadar yield eder.</summary>
    IAsyncEnumerable<LiveServerEvent> ReceiveEventsAsync(CancellationToken ct);
}
