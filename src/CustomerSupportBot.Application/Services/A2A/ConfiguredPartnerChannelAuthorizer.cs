// Application/Services/A2A/ConfiguredPartnerChannelAuthorizer.cs
// IPartnerChannelAuthorizer — A2A:Partners:*:Channels yapılandırmasından, VARSAYILAN REDDEDEN.

using CustomerSupportBot.Application.Ports.Outbound.A2A;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.A2A;

public sealed class ConfiguredPartnerChannelAuthorizer(IOptionsMonitor<A2AOptions> options) : IPartnerChannelAuthorizer
{
    /// <summary>
    /// <b>a2a</b> için geriye dönük uyumluluk: <c>Partner</c> rolündeki bir hesap <c>A2A:Partners</c>'ta tanımlı olmasa da
    /// ürün ajanını (müşteri verisi içermeyen katalog) eskiden kullanabiliyordu; tanımlı olmayan partner hâlâ kullanır.
    /// Müşteri verisi için zaten özne token'ı gerekir ve onu yalnızca tanımlı partner alabilir. Kanalı yapılandırmada
    /// AÇIKÇA kısıtlanmış partner reddedilir.
    ///
    /// <para>
    /// <b>mcp</b> (yeni kanal) için kural tersidir: açıkça verilmedikçe kapalıdır, tanımsız partner giremez.
    /// </para>
    /// </summary>
    public bool CanUse(string? partnerId, string channel)
    {
        if (string.IsNullOrWhiteSpace(partnerId)) return false;

        var partner = Find(partnerId);
        if (partner is null) return string.Equals(channel, PartnerChannels.A2A, StringComparison.OrdinalIgnoreCase);

        return partner.EffectiveChannels.Contains(channel, StringComparer.OrdinalIgnoreCase);
    }

    public bool CanUseAny(string? partnerId) => Find(partnerId)?.EffectiveChannels.Count > 0;

    // Eşleşme tam ve büyük/küçük harfe duyarlı (ConfiguredA2ASubjectAuthorizer ile aynı). Canlı okunur: IOptionsMonitor.
    private A2APartnerOptions? Find(string? partnerId) =>
        string.IsNullOrWhiteSpace(partnerId)
            ? null
            : options.CurrentValue.Partners.FirstOrDefault(p => string.Equals(p.PartnerId, partnerId, StringComparison.Ordinal));
}
