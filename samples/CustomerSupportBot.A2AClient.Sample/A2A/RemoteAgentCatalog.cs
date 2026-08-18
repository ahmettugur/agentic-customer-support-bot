// Uzak A2A ajanlarini kart uzerinden kesfeder ve AIAgent'a donusturur.

using A2A;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.A2AClient.Sample.A2A;

/// <summary>Bir uzak ajanin adresi, hangi token'i istedigi ve kartta beklenen yetenekleri.</summary>
public sealed record RemoteAgentSpec(string Slug, string HttpClientName, string[] RequiredSkillIds);

/// <summary>
/// AgentCard -> AIAgent donusumu (resmi "Catalog-Based Discovery" kalibi).
///
/// <para>
/// Kart yalnizca bir tanitim degildir: baglanilacak URL ve binding oradan okunur, kodda
/// sabitlenmez. Sabitlenseydi sunucu tarafinda adres degistiginde istemci sessizce yanlis
/// yere baglanmaya calisirdi.
/// </para>
/// </summary>
public sealed class RemoteAgentCatalog(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<RemoteAgentCatalog> logger)
{
    public static readonly RemoteAgentSpec Product   = new("product",   "a2a-partner", ["product-lookup", "product-list"]);
    public static readonly RemoteAgentSpec Order     = new("order",     "a2a-subject", ["order-status", "last-order", "order-list"]);
    public static readonly RemoteAgentSpec Complaint = new("complaint", "a2a-subject", ["complaint-status", "complaint-list"]);

    private readonly string _baseUrl = (config["A2A:BaseUrl"] ?? "http://localhost:5021").TrimEnd('/');

    /// <summary>
    /// Ajanı KARTIYLA BİRLİKTE döner. Kart, yalnızca yetenek doğrulaması için değil — çağıran
    /// tarafın yönlendirme metnini kartın skill'lerinden ÜRETMESİ için de gereklidir (bkz.
    /// <c>SupportAgentFactory</c>). Yalnızca <see cref="AIAgent"/> dönseydi, LLM'e hangi ajanın
    /// ne yapabildiğini anlatan metin elle yazılıp kartla senkron TUTULMAK zorunda kalırdı —
    /// sunucu tarafında bir skill değişince istemci sessizce eskirdi.
    /// </summary>
    public async Task<(AIAgent Agent, AgentCard Card)> ResolveAsync(RemoteAgentSpec spec, CancellationToken ct = default)
    {
        var http = httpFactory.CreateClient(spec.HttpClientName);

        // Kart yolu AJAN BASINADIR ve TAM verilmelidir. A2ACardResolver varsayilan
        // goreli cozumu uygular: taban /a2a/order iken son segmenti degistirip
        // /a2a/.well-known/agent-card.json ister ve 404 alir. Olculdu.
        var resolver = new A2ACardResolver(
            new Uri(_baseUrl), http, $"/a2a/{spec.Slug}/.well-known/agent-card.json");

        var card = await resolver.GetAgentCardAsync(ct);

        // Yetenek dogrulamasi: kart bekledigimiz skill'leri ilan etmiyorsa HEMEN dusuyoruz.
        // Kontrol olmasaydi eksiklik ancak kullanici o yetenegi isteyip anlamsiz bir cevap
        // aldiginda fark edilirdi — yani sessiz bir bozulma olurdu.
        var declared = card.Skills?.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var missing = spec.RequiredSkillIds.Where(id => !declared.Contains(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"'{card.Name}' karti beklenen yetenekleri ilan etmiyor: {string.Join(", ", missing)}. "
              + $"Kartta olanlar: {(declared.Count == 0 ? "(hic)" : string.Join(", ", declared))}");

        logger.LogInformation("Uzak ajan hazir: {Name} (yetenek: {Skills})", card.Name, string.Join(", ", declared));

        // httpClient GECILMEK ZORUNDA: gecilmezse AsAIAgent kendi ic HttpClient'ini kurar,
        // token handler'imiz o boru hattina eklenmez ve kart kesfi BASARILI olsa bile
        // mesaj gonderimi 401 alir — yani hata sebebinden uzakta patlar.
        var agent = card.AsAIAgent(
            httpClient: http,
            options: new A2AClientOptions { PreferredBindings = [ProtocolBindingNames.HttpJson] });

        return (agent, card);
    }
}
