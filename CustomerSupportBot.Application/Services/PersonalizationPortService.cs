// Application/Services/PersonalizationPortService.cs
// DRIVING PORT IMPL — IPersonalizationPort → CustomerProfileService + ICustomerProfileStore.

using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Application.Services;

public sealed class PersonalizationPortService : IPersonalizationPort
{
    private readonly CustomerProfileService _profileService;
    private readonly ICustomerProfileStore _profiles;

    public PersonalizationPortService(CustomerProfileService profileService, ICustomerProfileStore profiles)
    {
        _profileService = profileService;
        _profiles = profiles;
    }

    public (int Count, IReadOnlyList<CustomerProfile> Items) GetProfiles(int take = 100)
        => (_profiles.Count, _profiles.List(take));

    public CustomerProfile? GetProfile(string customerId)
        => _profiles.Get(customerId);

    public Task<CustomerProfile?> RefreshProfileAsync(string customerId, CancellationToken ct = default)
        => _profileService.ConsolidateAsync(customerId, ct);

    public CustomerProfile SetAdminNote(string customerId, string? note)
    {
        var profile = _profiles.GetOrCreate(customerId);
        profile.AdminNote = string.IsNullOrWhiteSpace(note) ? null : note;
        _profiles.Upsert(profile);
        return profile;
    }

    public bool DeleteProfile(string customerId)
        => _profiles.Delete(customerId);
}
