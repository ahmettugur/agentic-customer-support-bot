// Services/IContextProvider.cs
// Context Provider arayüzü — ajanlara bağlam bilgisi sağlar.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Ajan bağlam sağlayıcısı arayüzü.
/// Her provider, oturum durumuna göre ilgili bağlam bilgisini üretir.
/// </summary>
public interface IContextProvider
{
    /// <summary>Provider'ın adı (loglama ve debug için).</summary>
    string Name { get; }

    /// <summary>Çalışma önceliği (düşük sayı = yüksek öncelik).</summary>
    int Order { get; }

    /// <summary>
    /// Oturum durumuna göre bağlam metni üretir.
    /// Bağlam yoksa veya uygulanmıyorsa null döner.
    /// </summary>
    Task<string?> GetContextAsync(AgentSession session);
}
