// Infrastructure/Persistence/Entities/Hitl/HumanAgentEntity.cs
// hitl.human_agents tablosu — skills-based routing için insan temsilci kayıtları.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;

public sealed class HumanAgentEntity
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }

    /// <summary>Skills JSON dizisi — ["complaint","refund","vip"]</summary>
    public string SkillsJson { get; set; } = "[]";

    /// <summary>Languages JSON dizisi — ["tr","en"]</summary>
    public string LanguagesJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
    public int MaxConcurrentLoad { get; set; } = 5;
    public int CurrentLoad { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAssignedAt { get; set; }
}
