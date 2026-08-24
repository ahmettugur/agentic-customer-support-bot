// Tests/Agents/WorkflowRunnerEntityResolutionTests.cs
// Regresyon: "sipariş numaram 1042" → "peki 1043" gibi bağlam kelimesiz bir takip mesajında,
// WorkflowRunner'ın ENTITY EXTRACTION hint'i query-only IdExtractor.Extract'e düşüp 1043'ü
// customer_id sanmamalı — ReasoningService zaten EntityVerifier ile (query+history+authenticated
// session) doğru şekilde order_id olarak çözmüşse (reasoning.VerifiedEntities), WorkflowRunner bunu
// yeniden hesaplamak yerine AYNEN kullanmalı. Canlıda gözlemlenen bug: bu senkronizasyon
// olmadığı için specialist'e "customer_id MEVCUT → get_last_order_tool kullan" hint'i gidiyor,
// gerçek order_status_tool hiç çağrılmıyor, sipariş DB'de olmasına rağmen "bulunamadı" deniyordu.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class WorkflowRunnerEntityResolutionTests
{
    [Fact]
    public void ResolveExtractedIds_VerifiedEntitiesHasOrderId_PrefersItOverQueryOnlyExtraction()
    {
        // "peki 1043" tek başına (bağlam kelimesi yok, kısa mesaj) IdExtractor.Extract'te
        // customer_id'ye düşer — ama reasoning aşaması geçmişten ("sipariş numaram 1042" →
        // order bağlamı) doğru şekilde bunun bir order_id devamı olduğunu zaten çözmüş olsun.
        var reasoning = new ReasoningResult
        {
            VerifiedEntities = new VerifiedEntities
            {
                OrderId = new VerifiedEntity
                {
                    Value = "1043",
                    Source = EntitySource.History,
                    Verification = EntityVerification.Verified
                }
            }
        };

        var result = WorkflowMessageBuilder.ResolveExtractedIds("peki 1043", reasoning);

        result.OrderId.Should().Be("1043");
        result.CustomerId.Should().BeNull();
    }

    [Fact]
    public void ResolveExtractedIds_NoVerifiedEntities_FallsBackToQueryOnlyExtraction()
    {
        // reasoning null (bazı çağrı yolları reasoning'i atlıyor olabilir) — eski davranış korunur.
        var result = WorkflowMessageBuilder.ResolveExtractedIds("sipariş 1042 durumu", reasoning: null);

        result.OrderId.Should().Be("1042");
    }

    [Fact]
    public void ResolveExtractedIds_ReasoningPresentButNoVerifiedEntities_FallsBackToQueryOnlyExtraction()
    {
        var reasoning = new ReasoningResult(); // VerifiedEntities null

        var result = WorkflowMessageBuilder.ResolveExtractedIds("sipariş 1042 durumu", reasoning);

        result.OrderId.Should().Be("1042");
    }

    [Fact]
    public void ResolveExtractedIds_VerifiedEntitiesEmpty_FallsBackToQueryOnlyExtraction()
    {
        var reasoning = new ReasoningResult { VerifiedEntities = new VerifiedEntities() }; // HasAny=false

        // "peki 1043" bağlamsız kısa mesaj → query-only fallback customer_id varsayar.
        var result = WorkflowMessageBuilder.ResolveExtractedIds("peki 1043", reasoning);

        result.CustomerId.Should().Be("1043");
        result.OrderId.Should().BeNull();
    }

    [Fact]
    public void ResolveExtractedIds_VerifiedEntitiesHasAllThree_MapsAllFields()
    {
        var reasoning = new ReasoningResult
        {
            VerifiedEntities = new VerifiedEntities
            {
                OrderId = new VerifiedEntity { Value = "1030", Verification = EntityVerification.Verified },
                CustomerId = new VerifiedEntity { Value = "1027", Verification = EntityVerification.Verified },
                ComplaintId = new VerifiedEntity { Value = "1001", Verification = EntityVerification.Verified }
            }
        };

        var result = WorkflowMessageBuilder.ResolveExtractedIds("herhangi bir sorgu", reasoning);

        result.OrderId.Should().Be("1030");
        result.CustomerId.Should().Be("1027");
        result.ComplaintId.Should().Be("1001");
    }
}
