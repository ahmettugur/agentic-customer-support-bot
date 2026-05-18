// Services/RedisNodeId.cs
// Her pod başlangıcında oluşturulan benzersiz kimlik.
// Redis pub/sub mesajlarında "bu mesajı ben gönderdim" kontrolü için kullanılır.

namespace CustomerSupportBot.Api.Services;

internal static class RedisNodeId
{
    internal static readonly string Value = Guid.NewGuid().ToString("N");
}
