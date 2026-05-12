// Models/RedisOptions.cs
// Redis bağlantı ve distributed lock yapılandırması.
// appsettings.json → "Redis" bölümünden bind edilir.
// Redis zorunludur — uygulama başlatılırken connection string olmalıdır.

namespace CustomerSupportBot.Models;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// ConnectionStrings:Redis'ten de çekilebilir; bu alan override'dır.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Lock key prefix — multi-tenant ortamda çakışmayı önler.</summary>
    public string KeyPrefix { get; set; } = "csbot";

    /// <summary>Lock varsayılan zaman aşımı (saniye). Deadlock koruması.</summary>
    public int DefaultLockTimeoutSeconds { get; set; } = 10;

    /// <summary>Lock süresi / lease (saniye). Bu süre sonunda lock otomatik release olur.</summary>
    public int LockExpirySeconds { get; set; } = 30;
}
