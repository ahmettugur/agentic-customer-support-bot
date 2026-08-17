// Adapters.Redis/RedisOptions.cs
// Redis connection and distributed lock configuration.
// Bound from appsettings.json > "Redis". Redis is required — app startup fails without connection string.

namespace CustomerSupportBot.Adapters.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// May also come from ConnectionStrings:Redis; this field overrides it.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Lock key prefix — prevents collisions in multi-tenant environments.</summary>
    public string KeyPrefix { get; set; } = "csbot";

    /// <summary>Default lock timeout (seconds). Deadlock protection.</summary>
    public int DefaultLockTimeoutSeconds { get; set; } = 10;

    /// <summary>Lock lease duration (seconds). Lock auto-releases after this period.</summary>
    public int LockExpirySeconds { get; set; } = 30;
}
