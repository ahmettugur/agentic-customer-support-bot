// Uctan uca dogrulama modu (--verify).
//
// Etkilesimli ajan modunun yaninda KORUNDU, cunku farkli bir isi var: bu, LLM'e hic
// bagli olmadan zincirin butun halkalarini (giris -> token degisimi -> kart kesfi ->
// gercek A2A cagrisi -> yetki siniri) tek komutta olcer ve CI'da kullanilabilecek bir
// cikis kodu dondurur. Partner kimligi hatasini yakalayan da tam olarak bu moddu.

using System.Net;
using A2A;
using CustomerSupportBot.A2AClient.Sample.A2A;
using CustomerSupportBot.A2AClient.Sample.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.A2AClient.Sample.Verification;

public static class EndToEndCheck
{
    public static async Task<int> RunAsync(IServiceProvider sp, string baseUrl, CancellationToken ct)
    {
        var tokens = sp.GetRequiredService<A2ATokenProvider>();
        var catalog = sp.GetRequiredService<RemoteAgentCatalog>();
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        // Tüm koşuya tek bir correlation.id: Telemetry:Enabled=true iken bu, HTTP istemci
        // enstrümantasyonu üzerinden giden A2A çağrılarına W3C traceparent olarak yansır ve
        // sunucu tarafındaki AYNI iz altında görünür — beş adımın hepsi tek trace'te birleşir.
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        using var runActivity = SampleTelemetry.Source.StartActivity("verify-run");
        runActivity?.SetTag("correlation.id", correlationId);
        Console.WriteLine($"  [correlation.id={correlationId}]");

        void Step(string t) => Console.WriteLine($"\n== {t}");
        void Ok(string m) => Console.WriteLine($"   [OK]   {m}");
        void Fail(string m) => Console.WriteLine($"   [HATA] {m}");

        try
        {
            Step("1. Partner girisi");
            await tokens.GetPartnerTokenAsync(ct);
            Ok("Partner token alindi — urun ajanini cagirabilir ve token DEGISIMI yapabilir.");

            Step($"2. Token degisimi (musteri {tokens.CustomerId} adina)");
            await tokens.GetSubjectTokenAsync(ct);
            Ok("Ozne token'i alindi — TEK musteriye kilitli, kisa omurlu.");

            Step("3. Kart kesfi + yetenek dogrulamasi");
            foreach (var spec in new[] { RemoteAgentCatalog.Product, RemoteAgentCatalog.Order, RemoteAgentCatalog.Complaint })
            {
                var (agent, _) = await catalog.ResolveAsync(spec, ct);
                Ok($"{spec.Slug,-10} kart okundu, beklenen yetenekler ilan edilmis ({agent.Name}).");
            }

            Step("4. Gercek A2A cagrilari");
            foreach (var (spec, soru) in new[]
                     {
                         (RemoteAgentCatalog.Product,   "Cay fiyati nedir?"),
                         (RemoteAgentCatalog.Order,     "Son siparisim ne durumda?"),
                         (RemoteAgentCatalog.Complaint, "Sikayetlerimi listele.")
                     })
            {
                var (agent, _) = await catalog.ResolveAsync(spec, ct);
                var reply = await agent.RunAsync(soru, cancellationToken: ct);
                var text = reply.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(text)) { Fail($"{spec.Slug}: bos yanit"); return 1; }
                Ok($"{spec.Slug,-10} yanit: {(text.Length <= 160 ? text : text[..160] + "...")}");
            }

            Step("5. Negatif kontrol — partner token'i siparis ajanina erisememeli");
            try
            {
                var partnerHttp = httpFactory.CreateClient("a2a-partner");
                var a2a = new global::A2A.A2AClient(new Uri($"{baseUrl}/a2a/order"), partnerHttp);
                await a2a.SendMessageAsync(new SendMessageRequest
                {
                    Message = new Message
                    {
                        Role = Role.User,
                        MessageId = Guid.NewGuid().ToString("N"),
                        Parts = [Part.FromText("Siparislerimi listele")]
                    }
                }, cancellationToken: ct);

                Fail("BEKLENMEDIK: partner token'i siparis ajanina erisebildi. Yetki siniri calismiyor!");
                return 1;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                Ok($"beklendigi gibi reddedildi ({(int)ex.StatusCode!} {ex.StatusCode}).");
            }
            catch (Exception ex)
            {
                // Herhangi bir istisnayi "reddedildi" saymak bu kontrolu ISE YARAMAZ hale getirir:
                // var olmayan bir ajan ya da kapali sunucu da istisna atar ve kontrol yesil gorunur.
                // Olculdu: gercek yetki reddi 403, bozukluk 404 dondurur — ayrim yapilabilir.
                var sc = (ex as HttpRequestException)?.StatusCode;
                Fail($"BEKLENMEDIK hata: {ex.GetType().Name} StatusCode={(sc.HasValue ? ((int)sc).ToString() : "yok")}. "
                   + "Beklenen 401/403 idi — bu bir yetki reddi degil, baska bir ariza.");
                return 1;
            }

            Console.WriteLine("\nTum adimlar basarili.");
            return 0;
        }
        catch (Exception ex)
        {
            runActivity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            Fail(ex.Message);
            return 1;
        }
    }
}
