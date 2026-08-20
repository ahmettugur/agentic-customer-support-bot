// Mükerrer onay talebi tespitinin PARAMETRE İMZASI.
//
// Aynı session'da aynı imzalı bekleyen bir talep varsa yeni kayıt açılmaz — LLM tool çağrısını
// tekrarladığında iş iki kez yürütülmesin diye. Bu koruma imzanın parametreleri gerçekten
// AYIRT ETMESİNE dayanır; imza kaba olursa koruma, koruduğu şeyden daha çok zarar verir:
// farklı iki talep aynı sayılır ve ikincisi sessizce hiç oluşturulmaz.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents.Tests;

[Collection("PostgresCatalog")]
public class ApprovalDedupeSignatureTests
{
    private readonly PostgresCatalogFixture _fixture;

    public ApprovalDedupeSignatureTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private (ApprovalGateService Svc, InMemoryApprovalQueue Queue, IDisposable Scope) Build(string toolName)
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { toolName }
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts), new NoopApprovalExecutionRouter(),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var accessor = new ApprovalContextAccessor();
        var scope = accessor.SetScope("s-dedupe", null, "istek", "9011");
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);

        var svc = new ApprovalGateService(
            queue, Options.Create(opts), sink, accessor,
            TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo),
            new EscalationPolicyService(sink, Options.Create(opts)));

        return (svc, queue, scope);
    }

    /// <summary>
    /// ASIL BULGU. Sipariş satırları bir dizi olarak taşınıyor ve eski imza değerleri
    /// <c>ToString()</c> ile metne çeviriyordu — bir dizi için bu, İÇERİĞE bakmayan sabit bir
    /// tip adıdır. Sonuç: aynı session'daki her sipariş talebi aynı imzayı üretiyordu.
    ///
    /// <para>
    /// Kullanıcı açısından görünümü: "2 kahve sipariş et" onaya gider; ardından "5 çay sipariş
    /// et" dendiğinde ikinci talep mükerrer sayılıp hiç oluşturulmaz. Kullanıcıya yine
    /// "onaya gönderildi" denir, admin tek bir kaydı onaylar ve çay siparişi hiçbir zaman
    /// var olmaz — hiçbir yerde hata görünmeden.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TwoDifferentOrders_InTheSameSession_CreateTwoSeparateApprovals()
    {
        var (svc, queue, scope) = Build(WellKnown.ToolNames.OrderPlacement);
        using var _ = scope;
        var fn = svc.BuildOrderPlacementTool();

        var products = _fixture.ProductRepo.GetAll().Take(2).Select(p => p.Name).ToList();

        await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest(products[0], 2) }
        }), TestContext.Current.CancellationToken);

        await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest(products[1], 5) }
        }), TestContext.Current.CancellationToken);

        queue.GetPending()
            .Where(p => p.ToolName == WellKnown.ToolNames.OrderPlacement)
            .Should().HaveCount(2, "farklı ürünler için verilen iki sipariş ayrı taleplerdir");
    }

    /// <summary>Adet farkı da ayırt edilmeli — aynı ürün, farklı miktar ayrı bir siparıştir.</summary>
    [Fact]
    public async Task SameProductWithDifferentQuantity_CreatesASeparateApproval()
    {
        var (svc, queue, scope) = Build(WellKnown.ToolNames.OrderPlacement);
        using var _ = scope;
        var fn = svc.BuildOrderPlacementTool();
        var product = _fixture.ProductRepo.GetAll().First().Name;

        foreach (var qty in new[] { 1, 7 })
        {
            await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["lines"] = new[] { new OrderLineRequest(product, qty) }
            }), TestContext.Current.CancellationToken);
        }

        queue.GetPending().Should().HaveCount(2);
    }

    /// <summary>
    /// Korumanın kendisi çalışmaya devam etmeli: GERÇEKTEN aynı çağrı tekrarlandığında
    /// ikinci bir kayıt açılmamalı. Bu testi kaybetmek, düzeltmenin dedupe'u tamamen
    /// kapatmasıyla aynı şey olurdu.
    /// </summary>
    [Fact]
    public async Task TheIdenticalCallRepeated_StillCollapsesIntoOneApproval()
    {
        var (svc, queue, scope) = Build(WellKnown.ToolNames.OrderPlacement);
        using var _ = scope;
        var fn = svc.BuildOrderPlacementTool();
        var product = _fixture.ProductRepo.GetAll().First().Name;

        for (var i = 0; i < 2; i++)
        {
            await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["lines"] = new[] { new OrderLineRequest(product, 3) }
            }), TestContext.Current.CancellationToken);
        }

        queue.GetPending().Should().ContainSingle();
    }

    // NOT — sınanmayan bir kusur. Eski imza değerleri kaçışlamadan "|" ile birleştirdiği
    // için, teorik olarak farklı parametre sözlükleri aynı imzayı üretebilir (ölçüldü:
    // {a:"x|b=y"} ile {a:"x", b:"y"} aynı metni verir). Onay gerektiren dört tool'un anahtar
    // KÜMESİ sabit olduğundan bu çakışmayı gerçek bir çağrıyla tetiklemenin bir yolunu
    // bulamadım — bu yüzden buraya yeşil ama hiçbir şey ölçmeyen bir test bırakmıyorum.
    // JSON serileştirme kaçışı yapısal olarak sağlar; kusur tetiklenebilir hâle gelirse
    // (ör. tool'a opsiyonel parametre eklenirse) koruma zaten yerinde olur.
}
