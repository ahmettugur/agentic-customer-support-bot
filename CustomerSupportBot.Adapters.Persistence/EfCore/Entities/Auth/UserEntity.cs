// Infrastructure/Persistence/Entities/Auth/UserEntity.cs
// auth.users — admin kullanıcı tablosu.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;

public sealed class UserEntity
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Admin";
    /// <summary>Agent rolündeki kullanıcının bağlı olduğu HumanAgent kaydı (opsiyonel).</summary>
    public string? LinkedAgentId { get; set; }

    /// <summary>Customer rolündeki kullanıcının bağlı olduğu CustomerEntity.Id (opsiyonel).</summary>
    public string? LinkedCustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
