// Tests/Spikes/A2AHostingSpikeTests.cs
// A2A (Agent2Agent) SPIKE — go/no-go. ÜRETİM KODU DEĞİL: production graph'a hiç bağlı değil.
//
// Yanıtladığı TEK soru: MAF çekirdeği 1.17.0 iken, ona karşı DERLENMEMİŞ olan
// Microsoft.Agents.AI.Hosting.A2A(.AspNetCore) 1.12.0-preview çalışma zamanında ayakta kalıyor mu?
//
// Risk neden gerçek: NuGet, Microsoft.Agents.AI.Abstractions'ı yukarı birleştiriyor —
//   Microsoft.Agents.AI.Hosting.A2A.dll  → assembly sürümü 1.12.0.0
//   Microsoft.Agents.AI.Abstractions.dll → assembly sürümü 1.17.0.0  (birleştirilen)
// .NET Core assembly sürümünü katı bağlamadığı için bu DERLENİR ve YÜKLENİR; ama 1.12 ile 1.17
// arasında bir tip/imza değiştiyse hata ancak o kod yolu ÇALIŞTIRILDIĞINDA
// TypeLoadException / MissingMethodException olarak ortaya çıkar. Yani "dotnet build temiz"
// hiçbir şey kanıtlamaz — köprünün gerçekten çağrılması gerekir.
//
// Bu yüzden aşağıdaki test LLM'siz sahte bir AIAgent'ı MapA2AJsonRpc ile yayınlayıp üstüne
// gerçek bir A2A JSON-RPC isteği geçirir (TestServer). LLM/API key gerekmez — sahte ajan
// deterministik yanıt döner; ölçtüğümüz şey ajan zekâsı değil, KÖPRÜNÜN AYAKTA KALMASI.

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using A2A;

// Projede kendi ChatResponse'umuz (Application.Ports.Inbound) global using ile geldiği için
// MEAI'nin ChatResponse'u ile CS0104 çakışıyor — burada MEAI'ninki kastediliyor.
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class A2AHostingSpikeTests
{
    /// <summary>
    /// LLM'siz, deterministik IChatClient. Ajanı üretimdeki şekliyle (ChatClientAgent)
    /// kurabilmek için — AIAgent'ı elle türetmek yerine gerçek kompozisyonu taklit ediyoruz,
    /// böylece test ettiğimiz şey production'daki ajan tipiyle aynı sınıf.
    /// </summary>
    private sealed class EchoChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = string.Join(" ", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"echo: {text}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (var m in response.Messages)
                yield return new ChatResponseUpdate(m.Role, m.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static AIAgent BuildEchoAgent() =>
        new ChatClientAgent(new EchoChatClient(), new ChatClientAgentOptions
        {
            Name = "SpikeEchoAgent",
            Description = "A2A köprüsü ABI doğrulaması için sahte ajan."
        });

    private static async Task<(TestServer server, IHost host)> StartAsync()
    {
        var agent = BuildEchoAgent();

        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddRouting();
                    // MapA2AJsonRpc(AIAgent, path) ajanı ADIYLA DI'dan çözüyor — önce
                    // AddA2AServer ile kaydedilmesi şart. (İlk denemede bu eksikti ve
                    // köprü net bir InvalidOperationException ile uyardı; kayıt modeli
                    // "ajanı map ederken geçir" değil, "servis olarak kaydet, adıyla map et".)
                    s.AddA2AServer(agent);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e =>
                    {
                        // ⬅️ TEST EDİLEN SATIR: MAF köprüsü. Bu çağrı 1.12 hosting assembly'sinin
                        // 1.17 Abstractions'a karşı ayakta kalıp kalmadığını belirler.
                        e.MapA2AJsonRpc(agent, "/a2a");
                    });
                });
            })
            .StartAsync();

        return (host.GetTestServer(), host);
    }

    [Fact]
    public async Task MapA2AJsonRpc_AgainstMafCore117_DoesNotThrowOnStartup()
    {
        // Mapping'in kendisi bile 1.12/1.17 uyumsuzluğunda patlayabilir (tip yükleme anı).
        var (_, host) = await StartAsync();
        using var _h = host;

        // Buraya gelebildiysek köprü YÜKLENDİ — ilk ve en ucuz sinyal.
        true.Should().BeTrue();
    }

    [Fact]
    public async Task MessageSend_RoundTrips_ThroughMafA2ABridge()
    {
        var (server, host) = await StartAsync();
        using var _h = host;
        using var http = server.CreateClient();

        // JSON'u elle kurmak yerine SDK'nın KENDİ istemcisi kullanılıyor: hem protokol
        // ayrıntılarını (metot adı, zarf şekli) doğru kullanmayı garanti eder, hem de
        // client+server iki ucu birden aynı anda doğrular — gerçek entegrasyonun şekli budur.
        var client = new A2AClient(new Uri("http://localhost/a2a"), http);

        var response = await client.SendMessageAsync(
            new SendMessageRequest
            {
                Message = new Message
                {
                    Role = Role.User,
                    MessageId = Guid.NewGuid().ToString("N"),
                    Parts = [Part.FromText("merhaba")]
                }
            },
            TestContext.Current.CancellationToken);

        // Asıl kanıt: köprü çalıştı, ajana ulaştı, yanıt A2A zarfında geri geldi.
        response.Should().NotBeNull();
        var text = string.Join(" ", (response.Message?.Parts ?? []).Select(p => p.Text));
        text.Should().Contain("echo:", "ajanın çıktısı A2A yanıtına taşınmalı");
        text.Should().Contain("merhaba", "kullanıcı metni ajana ulaşmalı");
    }
}
