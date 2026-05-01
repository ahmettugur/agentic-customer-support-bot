// Infrastructure/Persistence/Entities/Auth/RefreshTokenEntity.cs
// auth.refresh_tokens — her oturum için opaque refresh token kaydı (hash'lenmiş).

namespace CustomerSupportBot.Infrastructure.Persistence.Entities.Auth;

public sealed class RefreshTokenEntity
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
