// Application/Services/Providers/CustomerProfileContextProvider.cs
// Login'li müşterinin (SessionState.AuthenticatedCustomerId — JWT'den) store'da profili varsa,
// kısa bir "Müşteri Profili" bloğu olarak context'e enjekte eder.
// SessionState.CustomerId (LLM'in metinden çıkardığı, kullanıcının değiştirebildiği alan)
// BİLEREK kullanılmaz — başkasının profilini (admin notu, geçmiş özeti dahil) sızdırırdı.

using System.Text;

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class CustomerProfileContextProvider : IContextProvider
{
    private readonly ICustomerProfileStore _store;
    private readonly ILogger<CustomerProfileContextProvider> _logger;

    public string Name => "CustomerProfile";

    /// <summary>
    /// CustomerContext (3) ile SemanticMemory (7) arasında — müşterinin uzun
    /// vadeli tercihleri, semantik knowledge'tan önce ajan dikkatini çekmeli.
    /// </summary>
    public int Order => 6;

    public CustomerProfileContextProvider(
        ICustomerProfileStore store,
        ILogger<CustomerProfileContextProvider> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery)
    {
        var customerId = session.State.AuthenticatedCustomerId;
        if (string.IsNullOrWhiteSpace(customerId)) return Task.FromResult<string?>(null);

        var profile = _store.Get(customerId);
        if (profile == null || profile.TotalTurns == 0) return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        sb.AppendLine("## 👤 Müşteri Profili");
        sb.AppendLine($"- ID: {profile.CustomerId}");
        if (!string.IsNullOrWhiteSpace(profile.Summary))
            sb.AppendLine($"- Özet: {profile.Summary}");
        if (!string.IsNullOrWhiteSpace(profile.AdminNote))
            sb.AppendLine($"- Admin notu: {profile.AdminNote}");
        sb.AppendLine($"- Tercih edilen dil: {profile.PreferredLanguage}, ton: {profile.PreferredTone}");
        sb.AppendLine($"- Toplam oturum: {profile.TotalSessions}, toplam tur: {profile.TotalTurns}");

        if (profile.IntentFrequency.Count > 0)
        {
            var top = profile.IntentFrequency
                .OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => $"{kv.Key} ({kv.Value})");
            sb.AppendLine($"- Sık niyetler: {string.Join(", ", top)}");
        }
        if (profile.ProductInterests.Count > 0)
        {
            sb.AppendLine($"- İlgi alanları: {string.Join(", ", profile.ProductInterests.Take(5))}");
        }
        if (profile.RecentRatings.Count > 0)
        {
            sb.AppendLine($"- Son ortalama puan: {profile.RecentRatings.Average():F1}/5 ({profile.RecentRatings.Count} oturum)");
        }

        _logger.LogDebug("CustomerProfile context: customerId={Id} turns={Turns}", customerId, profile.TotalTurns);
        return Task.FromResult<string?>(sb.ToString());
    }
}
