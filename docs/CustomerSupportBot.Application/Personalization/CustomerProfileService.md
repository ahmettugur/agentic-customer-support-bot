# CustomerProfileService

**Dosya:** `Services/Personalization/CustomerProfileService.cs`
**Port:** `ICustomerProfileService`

## 1. Ne İşe Yarar

Müşteri başına kalıcı profil — tercihler, ton, ilgi alanları, davranışsal çıkarımlar. İki ayrı
güncelleme yolu:

| Yol | Ne zaman | Maliyet |
|---|---|---|
| `RecordInteractionAsync` | Her tur sonu, otomatik | Sıfır LLM — kural tabanlı sayaç güncellemesi |
| `ConsolidateAsync` | Admin tetikler | Bir LLM çağrısı — `Summary`/`PreferredTone`/`Traits` üretir |

Bu ayrım bilinçli: episodik bellek yazımı da her turda LLM çağırmıyor, profil de aynı maliyet
sınıfında kalmalı.

## 2. `Traits` — çıkarım, olgu değil

`ConsolidateAsync`, `CustomerProfile.Traits`'i (`List<InferredTrait>`) LLM'e ürettirir:
*"Fiyat hassasiyeti yüksek"* gibi davranışsal gözlemler, her biri `Confidence` (0–1) ve
`Source` taşır.

> 🐞 **Bulundu ve düzeltildi — çıkarımlar olgu gibi sunuluyordu.** `Summary` alanı LLM
> tarafından üretiliyordu ama düz `string?` idi: hangi veriye dayandığı, ne kadar güvenilir
> olduğu hiçbir yerde tutulmuyordu. Bir öneri motoru (bkz. genel Customer Memory tasarımı)
> "fiyat hassasiyeti yüksek" gibi bir çıkarıma dayanarak davranış değiştirecekse, bunun bir
> TAHMİN olduğunu ve ne kadar veriye dayandığını bilmesi gerekir — yoksa zayıf bir tahmin
> kesin bir olgu gibi işlenir.

**`Source` formatı:** `consolidate:{customerId}@turn{totalTurns}` — hangi consolidate
çağrısından ve kaç tur birikmiş veriden türediğini taşır. "Bu çıkarım ne kadar veriye
dayanıyor?" sorusunun cevabı.

**Traits BİRİKMEZ — her `ConsolidateAsync` çağrısı baştan üretir.** Profil artımlı delta
değil, birikmiş sayaçlardan (`IntentFrequency`, `ProductInterests`, `RecentRatings`)
oluşuyor; `BuildConsolidatePrompt` her seferinde TÜM birikmiş veriyi LLM'e veriyor. Trait'ler
biriktirilseydi, 6 ay önce doğru olup artık geçersiz bir iddia sonsuza kadar profilde kalır ve
yenisiyle çelişirdi. Bedeli: iki consolidate arasında trait sayısı artmaz, öncekiler tamamen
değiştirilir.

`ParseTraits` savunmacıdır — LLM çıktısı güvenilmez veri sayılır: boş `claim` atlanır,
`confidence` `[0,1]`'e kırpılır (LLM aralık dışı veya string dönebilir), en fazla 5 trait
kabul edilir (`MaxTraits`). Sistem promptu LLM'e *"az veri varsa düşük confidence ver,
uydurma"* talimatı verir — ama bu bir istek, garanti değil; kırpma gerçek korumadır.

## 3. Nerede görünür

`CustomerProfileContextProvider` doğrudan `CustomerProfile`'ı okumaz — aradaki sentez katmanı
[`CustomerUnderstandingService`](CustomerUnderstandingService.md) `Traits`'i confidence'la
birlikte bir `CustomerUnderstanding` nesnesine çevirir (*"Fiyat hassasiyeti yüksek (güven:
%80)"*), provider da bunu metne döker. Bkz. [ContextProviders.md](../Providers/ContextProviders.md).

## Bağlantılar

- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — `Traits`'in bağlama enjekte edildiği yer
- [PersonalizationPortService.md](PersonalizationPortService.md) — Kişiselleştirme orkestratörü
- Domain modeli: `CustomerSupportBot.Domain/Model/Memory/CustomerProfile.cs` (+ `InferredTrait.cs`)
