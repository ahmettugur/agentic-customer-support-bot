// Infrastructure/Persistence/Entities/Personalization/CustomerProfileEntity.cs
// `personalization.customer_profiles` tablosu.
// Koleksiyonlar (IntentFrequency, ProductInterests, RecentRatings) JSONB olarak saklanır.

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Personalization;

public sealed class CustomerProfileEntity
{
    public string CustomerId { get; set; } = "";
    public string PreferredLanguage { get; set; } = "tr";
    public string PreferredTone { get; set; } = "neutral";

    public string IntentFrequencyJson { get; set; } = "{}";
    public string ProductInterestsJson { get; set; } = "[]";
    public string RecentRatingsJson { get; set; } = "[]";

    public string TraitsJson { get; set; } = "[]";

    public string? Summary { get; set; }
    public string? AdminNote { get; set; }
    public int TotalSessions { get; set; }
    public int TotalTurns { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastInteractionAt { get; set; }
    public DateTime? LastConsolidatedAt { get; set; }
}
