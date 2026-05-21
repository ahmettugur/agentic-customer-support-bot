// Adapters.Redis/ExceptionTranslator.cs
// Redis infrastructure exception'larını domain exception'larına çevirir.

using CustomerSupportBot.Domain.Exceptions;
using StackExchange.Redis;

namespace CustomerSupportBot.Adapters.Redis;

/// <summary>
/// Redis exception'larını domain exception'larına çevirir.
/// </summary>
internal static class ExceptionTranslator
{
    public static DomainException Translate(Exception ex, string? context = null)
    {
        return ex switch
        {
            RedisConnectionException =>
                new ExternalServiceException("Redis",
                    context ?? "Redis bağlantısı kurulamadı.", ex),

            RedisTimeoutException =>
                new ExternalServiceException("Redis",
                    context ?? "Redis işlemi zaman aşımına uğradı.", ex),

            RedisServerException { Message: var msg } when msg.Contains("BUSY") =>
                new ExternalServiceException("Redis",
                    context ?? "Redis sunucusu meşgul.", ex),

            _ => new ExternalServiceException("Redis",
                    context ?? "Redis işlemi başarısız oldu.", ex)
        };
    }
}
