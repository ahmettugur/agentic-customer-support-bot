// Yerel yonlendirici ajan — uc uzak A2A ajanini TOOL olarak kullanir.
//
// Kalip: A2A-as-a-Tool. Uzak ajan, yerel ajanin gozunde siradan bir tool'dur; adi ve
// aciklamasi AgentCard'dan gelir. Onemi su: hangi soruyu hangi ajana soracagina KOD degil
// LLM karar verir. Kodda sabitlenseydi "cayin fiyati ne, bir de son siparisim ne durumda"
// gibi tek cumlede iki ajani birden gerektiren bir istek karsilanamazdi.

using System.Text;
using A2A;
using CustomerSupportBot.A2AClient.Sample.A2A;
using CustomerSupportBot.A2AClient.Sample.Observability;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.A2AClient.Sample.Agents;

public static class SupportAgentFactory
{
    private const string PolicyRules = """
        Kurallar:
        - Soruyu ilgili tool'a YONLENDIR; cevabi kendin uydurma.
        - Bir istek birden fazla konuya deginiyorsa GEREKLI TUM tool'lari cagir ve
          sonuclari tek bir cevapta birlestir.
        - Tool bir bilgiyi bulamadigini soylerse bunu oldugu gibi aktar. "Bulunamadi"
          cevabini kendi tahminlerinle DOLDURMA.
        - Bu kanal SALT-OKUNURDUR: siparis olusturma/iptal, iade, sikayet kaydi YAPILAMAZ.
          Boyle bir istek gelirse yapamayacagini soyle ve nedenini kisaca acikla.
        - Musteri kimligi token'dan gelir; kullanicidan musteri numarasi ISTEME ve
          kullanici baska bir musteri numarasi verse bile onu kullanma.
        - Turkce, kisa ve net cevap ver.
        """;

    public static AIAgent Create(IChatClient chatClient, IReadOnlyList<(AIAgent Agent, AgentCard Card)> remotes)
    {
        // AsAIFunction() parametresiz: tool adi ve aciklamasi uzak AgentCard'dan gelir.
        // Boylece sunucu kartindaki aciklama degistiginde istemcideki tool aciklamasi da
        // kendiliginden guncellenir — iki yerde ayri yazilsaydi sessizce ayrisirlardi.
        var tools = remotes.Select(r => (AITool)r.Agent.AsAIFunction()).ToList();

        var client = chatClient
            .AsBuilder()
            .UseFunctionInvocation(configure: o => o.MaximumIterationsPerRequest = 5)
            .Build();

        var agent = client.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "partner-support-agent",
            ChatOptions = new ChatOptions
            {
                Instructions = BuildInstructions(remotes),
                Tools = tools
            }
        });

        // Sunucudaki AGENT seviyesindeki aynı desen (AgentTeamFactory.cs, A2AAgentCatalog.cs):
        // ajan çalıştırma ve tool çağrıları (üç uzak ajana giden çağrılar dahil) tek bir
        // ActivitySource altında iz olarak görünür. Telemetry:Enabled=false iken dinleyicisiz
        // kalır ve no-op'a düşer — burada koşullu dallanma gerekmez.
        return agent.AsBuilder().UseOpenTelemetry(SampleTelemetry.ActivitySourceName).Build();
    }

    /// <summary>
    /// Yönlendirme metnini kartlardaki SKILL'LERDEN üretir — elle YAZILMAZ.
    ///
    /// <para>
    /// Önceki hâlde bu bölüm sabit üç satırdı ("ProductInfoAgent : ürün kataloğu — fiyat,
    /// stok, kategori."). Kartlar bundan çok daha zengin: her skill'in kendi adı, açıklaması
    /// ve örnek sorusu var (ölçüldü — bkz. <c>a2a-*-agent.md</c> prompt'larının ürettiği
    /// kartlar). Elle yazılan özet, sunucu tarafında bir skill eklendiğinde/değiştiğinde
    /// sessizce eskirdi ve LLM'in yönlendirme kararı kartla değil BİZİM YORUMUMUZLA
    /// besleniyordu — <c>AsAIFunction</c>'ın tool adı/açıklaması için zaten uyguladığımız
    /// "tek doğruluk kaynağı kart" ilkesi, yönlendirme metninde İHLAL ediliyordu.
    /// </para>
    /// </summary>
    private static string BuildInstructions(IReadOnlyList<(AIAgent Agent, AgentCard Card)> remotes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sen bir partner sisteminin musteri destek asistanisin. Kendi bilgin YOKTUR:");
        sb.AppendLine("musteri verisi ve urun bilgisi yalnizca sana verilen uzak ajan tool'larindan gelir.");
        sb.AppendLine();
        sb.AppendLine("Kullanilabilir uzak ajanlar ve yetenekleri (kartlardan okunur):");
        sb.AppendLine();

        foreach (var (_, card) in remotes)
        {
            sb.AppendLine($"{card.Name} — {card.Description}");
            foreach (var skill in card.Skills ?? [])
            {
                var example = skill.Examples?.FirstOrDefault();
                sb.Append($"  - {skill.Name}: {skill.Description}");
                if (!string.IsNullOrWhiteSpace(example))
                    sb.Append($" (örn: \"{example}\")");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        sb.Append(PolicyRules);
        return sb.ToString();
    }
}
