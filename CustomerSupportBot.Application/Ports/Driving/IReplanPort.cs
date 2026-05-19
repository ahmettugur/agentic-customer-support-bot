// Application/Ports/Driving/IReplanPort.cs
// PRIMARY PORT — Bir session için bot'un yeniden planlama yapması ve
// müşteriye yanıt yayınlaması kullanım senaryosu.

namespace CustomerSupportBot.Application.Ports.Driving;

/// <summary>
/// Replan use case: session'daki son kullanıcı mesajını yeniden değerlendirir,
/// bot yanıtı üretir ve bridge aracılığıyla müşteriye iletir.
/// </summary>
public interface IReplanPort
{
    /// <summary>
    /// Arka planda son müşteri mesajı için reasoning + workflow koşturur.
    /// Bot yanıtını ChatBridge üzerinden bot mesajı olarak yayınlar.
    /// </summary>
    Task ExecuteAsync(string sessionId, CancellationToken ct = default);
}
