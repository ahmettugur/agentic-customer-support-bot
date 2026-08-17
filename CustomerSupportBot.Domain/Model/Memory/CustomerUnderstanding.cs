// Models/Memory/CustomerUnderstanding.cs
// Memory'nin üç kaynağından (yapısal olgular, profil, çıkarımlar) sentezlenmiş TEK görünüm.

namespace CustomerSupportBot.Domain.Model.Memory;

/// <summary>
/// Bir müşterinin <see cref="CustomerProfile"/>'dan sentezlenmiş, agent'ların doğrudan
/// okuyacağı tek görünümü.
///
/// <para>
/// <b>Bu tip neden ayrı, neden <see cref="CustomerProfile"/>'ın kendisi değil:</b>
/// <c>CustomerProfile</c> depolama şeklidir (Postgres satırı, JSONB sütunları,
/// artımlı sayaçlar). <c>CustomerUnderstanding</c> ise <b>okuma anındaki sentezdir</b> —
/// "bu profil şu an null/boş olabilir, tur sıfır olabilir" gibi depolama detaylarını
/// gizler ve tüketiciye (context provider, ileride öneri motoru) hep geçerli bir nesne
/// ya da <c>null</c> sunar. İki farklı tüketicinin aynı null-kontrol/varsayılan-değer
/// mantığını iki kez yazmasını önler — bugün <c>CustomerProfileContextProvider</c>, yarın
/// bir Recommendation bileşeni aynı sentezi kullanacak.
/// </para>
///
/// <para>
/// <b>Bilerek TAŞIMADIĞI şey:</b> anlık niyet/duygu/faz. Bunlar zaten
/// <c>WorkflowMessageBuilder.BuildReasoningSummaryHint</c> ile (o turun <c>ReasoningResult</c>'ından)
/// ayrı olarak bağlama giriyor. Burada tekrarlamak iki kaynağın (session'ın "şu an" durumu ile
/// profilin "genelde" durumu) çelişebileceği bir belirsizlik yaratırdı — hangisi doğru?
/// Understanding yalnızca UZUN VADELİ sentezi taşır; anlık olan başka yerin işi.
/// </para>
/// </summary>
/// <param name="CustomerId">Müşteri kimliği.</param>
/// <param name="Persona">LLM'in ürettiği 1-2 cümlelik özet (<see cref="CustomerProfile.Summary"/>). Hiç consolidate edilmemişse null.</param>
/// <param name="AdminNote">Admin'in elle eklediği override not.</param>
/// <param name="Traits">Confidence+source taşıyan davranışsal çıkarımlar.</param>
/// <param name="PreferredTone">"formal" | "casual" | "concise" | "verbose" | "neutral".</param>
/// <param name="PreferredLanguage">ISO 639-1.</param>
/// <param name="ProductInterests">İlgilenilen ürünler, en yenisi başta.</param>
/// <param name="TopIntents">En sık niyetler, azalan sıklık sırasında.</param>
/// <param name="AverageRating">Son oturumların ortalama puanı; hiç puan yoksa null.</param>
/// <param name="RatingCount">Ortalamaya giren puan sayısı.</param>
/// <param name="TotalSessions">Toplam oturum sayısı.</param>
/// <param name="TotalTurns">Toplam tur sayısı.</param>
/// <param name="LastConsolidatedAt">
/// <see cref="Persona"/>/<see cref="Traits"/>'in en son ne zaman üretildiği — TAZELİK sinyali.
/// Hiç consolidate edilmemişse null; bu durumda Persona/Traits boş olur ama
/// ProductInterests/TopIntents (heuristik, her turda güncellenir) yine de günceldir.
/// </param>
public sealed record CustomerUnderstanding(
    string CustomerId,
    string? Persona,
    string? AdminNote,
    IReadOnlyList<InferredTrait> Traits,
    string PreferredTone,
    string PreferredLanguage,
    IReadOnlyList<string> ProductInterests,
    IReadOnlyList<(string Intent, int Count)> TopIntents,
    double? AverageRating,
    int RatingCount,
    int TotalSessions,
    int TotalTurns,
    DateTime? LastConsolidatedAt);
