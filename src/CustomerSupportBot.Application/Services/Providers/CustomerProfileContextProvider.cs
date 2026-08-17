// Application/Services/Providers/CustomerProfileContextProvider.cs
// Login'li müşterinin (SessionState.AuthenticatedCustomerId — JWT'den) sentezlenmiş
// CustomerUnderstanding'i varsa, kısa bir "Müşteri Profili" bloğu olarak context'e enjekte eder.
// SessionState.CustomerId (LLM'in metinden çıkardığı, kullanıcının değiştirebildiği alan)
// BİLEREK kullanılmaz — başkasının profilini (admin notu, geçmiş özeti dahil) sızdırırdı.
//
// Sentez mantığı burada değil ICustomerUnderstandingService'te yaşar — bu sınıfın tek işi
// CustomerUnderstanding'i metne çevirmektir. Bkz. o servisin XML dokümanı: tek doğruluk
// kaynağı olması, ileride başka bir tüketicinin (öneri motoru) aynı null-kontrol/sıralama
// mantığını tekrar yazmasını önler.

using System.Text;

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

public sealed class CustomerProfileContextProvider : IContextProvider
{
    private readonly ICustomerUnderstandingService _understanding;
    private readonly ILogger<CustomerProfileContextProvider> _logger;

    public string Name => "CustomerProfile";

    /// <summary>
    /// CustomerContext (3) ile SemanticMemory (7) arasında — müşterinin uzun
    /// vadeli tercihleri, semantik knowledge'tan önce ajan dikkatini çekmeli.
    /// </summary>
    public int Order => 6;

    public CustomerProfileContextProvider(
        ICustomerUnderstandingService understanding,
        ILogger<CustomerProfileContextProvider> logger)
    {
        _understanding = understanding;
        _logger = logger;
    }

    public Task<string?> GetContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)
    {
        var u = _understanding.Build(session);
        if (u is null) return Task.FromResult<string?>(null);

        var sb = new StringBuilder();
        sb.AppendLine("## 👤 Müşteri Profili");
        sb.AppendLine($"- ID: {u.CustomerId}");
        if (!string.IsNullOrWhiteSpace(u.Persona))
            sb.AppendLine($"- Özet: {u.Persona}");
        if (!string.IsNullOrWhiteSpace(u.AdminNote))
            sb.AppendLine($"- Admin notu: {u.AdminNote}");
        sb.AppendLine($"- Tercih edilen dil: {u.PreferredLanguage}, ton: {u.PreferredTone}");
        sb.AppendLine($"- Toplam oturum: {u.TotalSessions}, toplam tur: {u.TotalTurns}");

        if (u.TopIntents.Count > 0)
        {
            var top = u.TopIntents.Select(t => $"{t.Intent} ({t.Count})");
            sb.AppendLine($"- Sık niyetler: {string.Join(", ", top)}");
        }
        if (u.ProductInterests.Count > 0)
        {
            sb.AppendLine($"- İlgi alanları: {string.Join(", ", u.ProductInterests)}");
        }
        if (u.AverageRating is { } avg)
        {
            sb.AppendLine($"- Son ortalama puan: {avg:F1}/5 ({u.RatingCount} oturum)");
        }
        if (u.Traits.Count > 0)
        {
            // Confidence açıkça yazılır: bu bir ÇIKARIM, olgu değil — model buna göre
            // "kesin biliyorum" yerine "muhtemelen" diliyle yaklaşmalı.
            sb.AppendLine("- Davranışsal gözlemler (çıkarım, kesin değil):");
            foreach (var trait in u.Traits.OrderByDescending(t => t.Confidence))
                sb.AppendLine($"  - {trait.Claim} (güven: %{trait.Confidence * 100:F0})");
        }

        _logger.LogDebug("CustomerProfile context: customerId={Id} turns={Turns}", u.CustomerId, u.TotalTurns);
        return Task.FromResult<string?>(sb.ToString());
    }
}
