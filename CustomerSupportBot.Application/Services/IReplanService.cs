// Application/Services/IReplanService.cs
// Application-internal strateji arayüzü — bir session için bot'un yeniden planlama
// yapması ve müşteriye yanıt yayınlaması use case'ini tanımlar.
//
// NOT: Bu arayüz Application-internal bir soyutlamadır — hiçbir adapter veya endpoint
// bunu tüketmez. Yalnızca ChatSessionPortService (Application) çağırır.
// Port değildir; Services/ altında doğru konumdadır.

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Replan use case: session'daki son kullanıcı mesajını yeniden değerlendirir,
/// bot yanıtı üretir ve bridge aracılığıyla müşteriye iletir.
/// </summary>
public interface IReplanService
{
    /// <summary>
    /// Arka planda son müşteri mesajı için reasoning + workflow koşturur.
    /// Bot yanıtını ChatBridge üzerinden bot mesajı olarak yayınlar.
    /// </summary>
    Task ExecuteAsync(string sessionId, CancellationToken ct = default);
}
