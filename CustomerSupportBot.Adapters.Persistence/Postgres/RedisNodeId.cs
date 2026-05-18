// Postgres/RedisNodeId.cs
// Her pod başlangıcında oluşturulan benzersiz kimlik.
// Redis pub/sub mesajlarında "bu mesajı ben gönderdim" kontrolü için kullanılır.

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

internal static class RedisNodeId
{
    internal static readonly string Value = Guid.NewGuid().ToString("N");
}
