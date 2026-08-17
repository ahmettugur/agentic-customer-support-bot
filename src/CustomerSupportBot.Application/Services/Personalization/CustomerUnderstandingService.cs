// Application/Services/Personalization/CustomerUnderstandingService.cs
// CustomerProfile'ı ICustomerUnderstandingService port'u üzerinden sentezler.
// LLM ÇAĞIRMAZ — saf, senkron bir dönüşümdür; maliyet sınıfı RecordInteractionAsync'le aynı.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services.Personalization;

public sealed class CustomerUnderstandingService : ICustomerUnderstandingService
{
    private const int MaxProductInterestsShown = 5;
    private const int MaxTopIntentsShown = 3;

    private readonly ICustomerProfileStore _store;

    public CustomerUnderstandingService(ICustomerProfileStore store) => _store = store;

    public CustomerUnderstanding? Build(AgentSession session)
    {
        var customerId = session.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return null;

        var profile = _store.Get(customerId);
        if (profile is null || profile.TotalTurns == 0) return null;

        return new CustomerUnderstanding(
            CustomerId: profile.CustomerId,
            Persona: profile.Summary,
            AdminNote: profile.AdminNote,
            Traits: profile.Traits,
            PreferredTone: profile.PreferredTone,
            PreferredLanguage: profile.PreferredLanguage,
            ProductInterests: profile.ProductInterests.Take(MaxProductInterestsShown).ToList(),
            TopIntents: profile.IntentFrequency
                .OrderByDescending(kv => kv.Value)
                .Take(MaxTopIntentsShown)
                .Select(kv => (kv.Key, kv.Value))
                .ToList(),
            AverageRating: profile.RecentRatings.Count > 0 ? profile.RecentRatings.Average() : null,
            RatingCount: profile.RecentRatings.Count,
            TotalSessions: profile.TotalSessions,
            TotalTurns: profile.TotalTurns,
            LastConsolidatedAt: profile.LastConsolidatedAt);
    }
}
