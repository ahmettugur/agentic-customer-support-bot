// Models/Memory/InferredTrait.cs
// CustomerProfile.Traits alanının elemanı — müşteri davranışından ÇIKARILMIŞ bir iddia.

namespace CustomerSupportBot.Domain.Model.Memory;

/// <summary>
/// Müşteri davranışından LLM tarafından çıkarılmış tek bir iddia — kesin gerçek olarak değil,
/// güven skoru ve kaynağıyla birlikte taşınır.
///
/// <para>
/// Bu tip <see cref="CustomerProfile"/>'ın diğer alanlarından (<c>PreferredTone</c>,
/// <c>ProductInterests</c>) kasıtlı olarak ayrıdır: onlar ya deterministik sayaçlardır
/// (<c>IntentFrequency</c>) ya da ham gözlemdir (hangi ürünler soruldu). <c>InferredTrait</c>
/// ise bunlardan LLM'in ürettiği bir YORUM'dur — "fiyat hassasiyeti yüksek" gibi bir gözlem
/// değil, bir çıkarımdır ve yanlış olabilir. Confidence/Source olmadan bu ayrım kaybolur ve
/// tahmin, olgu gibi sunulmuş olur.
/// </para>
/// </summary>
/// <param name="Claim">Kısa, tek cümlelik iddia (ör. "Fiyat hassasiyeti yüksek").</param>
/// <param name="Confidence">0.0–1.0 arası güven skoru. LLM'in kendi beyanı; doğrulanmamıştır.</param>
/// <param name="Source">
/// İddianın hangi consolidate çağrısından ve kaç tur birikmiş veriden türediği
/// (ör. <c>"consolidate@turn42"</c>) — "bu çıkarım ne kadar veriye dayanıyor" sorusunun cevabı.
/// </param>
/// <param name="InferredAt">Çıkarımın üretildiği an.</param>
public sealed record InferredTrait(
    string Claim,
    double Confidence,
    string Source,
    DateTime InferredAt);
