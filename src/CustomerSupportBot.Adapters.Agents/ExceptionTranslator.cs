// Adapters.Agents/ExceptionTranslator.cs
// Microsoft.Agents framework exception'larını domain exception'larına çevirir.

using CustomerSupportBot.Domain.Exceptions;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Agents framework exception'larını domain exception'larına çevirir.
/// </summary>
internal static class ExceptionTranslator
{
    /// <summary>
    /// Verilen exception'ı, kullanıcıya/istemciye güvenle gösterilebilecek <paramref name="context"/>
    /// metniyle bir <see cref="ExternalServiceException"/>'a sarar.
    ///
    /// <para>
    /// 🐞 <b>Eskiden burada exception türüne göre dallanan bir switch vardı</b>
    /// (<c>InvalidOperationException</c>, <c>TaskCanceledException{InnerException:TimeoutException}</c>,
    /// <c>OperationCanceledException</c>, <c>HttpRequestException</c>). Ölçüldü: her dal AYNI
    /// <see cref="ExternalServiceException"/> tipini döndürüyordu — yalnızca <paramref name="context"/>
    /// <c>null</c> olduğunda kullanılan FALLBACK mesaj metni farklıydı. Repo genelindeki tüm çağrı
    /// yerleri her zaman bir context geçiyordu (<c>context ?? ...</c> deseninde context her zaman
    /// kazanıyordu), yani switch'in hiçbir dalı pratikte hiçbir zaman gözlemlenebilir bir fark
    /// yaratmıyordu — "tür bazlı çeviri" görünümü altında ölü koddu. Sadeleştirildi.
    /// </para>
    /// </summary>
    public static DomainException Translate(Exception ex, string? context = null) =>
        new ExternalServiceException("AgentWorkflow", context ?? $"Ajan workflow hatası: {ex.Message}", ex);
}
