# VerifiedEntities

**Dosya:** `Model/VerifiedEntities.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `VerifiedEntity`, `EntitySource` enum, `EntityVerification` enum (aynı dosyada)

## 1. Ne İşe Yarar

`EntityVerifier`'ın authenticated session'dan (`AuthenticatedCustomerId` — JWT'den) ürettiği
**çözümlenmiş müşteri kimliği sonucu**dur. Reasoning prompt'una enjekte edilir. `OrderId`/
`ComplaintId` alanları `EntityVerifier` tarafından artık doldurulmaz (bkz. `IdExtractor`'ın
kaldırılması — order_id/complaint_id çözümü tamamen LLM'e bırakıldı); bu iki alan yalnızca
başka bir kaynaktan (ör. `SubTaskOrchestrator`'ın alt-görev decompose sırasında ürettiği
yapılandırılmış entity'lerden) doldurulabilir. Sipariş/şikayet gerçekliği ve sahipliği bu
modelde değil, specialist tool sonucunda doğrulanır.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.VerifiedEntities` olarak taşınır. `BuildPromptBlock()` ile LLM'in okuyacağı
`[RESOLVED ENTITIES]` bloğuna dönüştürülür. Entity türü/değeri için tek resolution kaynağıdır;
factual iş verisinin doğruluk kaynağı ilgili tool sonucudur.

> `order_id=1030 [FORMAT_ONLY]`, kullanıcının bu numarayı sağladığı anlamına gelir; siparişin
> var olduğu veya kullanıcıya ait olduğu anlamına gelmez. Tool sonucu gelmeden durum/ürün
> bilgisi üretilemez.

## 3. Metotlar / Üyeler

### VerifiedEntities

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `OrderId` | `VerifiedEntity?` | `EntityVerifier` doldurmaz; yalnızca `SubTaskOrchestrator` gibi başka kaynaklardan gelebilir |
| `CustomerId` | `VerifiedEntity?` | Authenticated session müşteri kimliği (`EntityVerifier`'ın tek ürettiği alan) |
| `ComplaintId` | `VerifiedEntity?` | `EntityVerifier` doldurmaz; yalnızca `SubTaskOrchestrator` gibi başka kaynaklardan gelebilir |
| `DerivedLastOrderId` | `string?` | Geriye dönük uyumluluk; resolver artık üretmez |
| `DerivedOrderCount` | `int?` | Geriye dönük uyumluluk; resolver artık üretmez |
| `HasAny` | `bool` | **Computed** — çözümlenmiş entity var mı? |
| `HasAnyVerified` | `bool` | **Computed** — güvenilir sistem kaynağıyla doğrulanmış entity var mı? |

### VerifiedEntity

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Value` | `string` | Entity değeri (ör. "1030") |
| `Source` | `EntitySource` | Nereden çıkarıldı |
| `Verification` | `EntityVerification` | Doğrulama seviyesi |
| `Attributes` | `Dictionary<string, string>?` | Uyumluluk alanı; resolver doldurmaz |

### EntitySource Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Query` | Güncel kullanıcı mesajından — `EntityVerifier` artık üretmez, geriye dönük uyumluluk için tanımlı kalır |
| `History` | Önceki konuşma turundan — `EntityVerifier` artık üretmez, geriye dönük uyumluluk için tanımlı kalır |
| `SessionState` | Oturum durumundan (AuthenticatedCustomerId) — `EntityVerifier`'ın tek kullandığı değer |
| `Derived` | Başka entity'den türetilmiş (ör. `SubTaskOrchestrator`'ın alt-görev entity'lerinden) |

### EntityVerification Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Verified` | Güvenilir sistem kaynağı (bugün authenticated customer identity) |
| `NotFoundInDb` | Geriye dönük/harici doğrulayıcı sonucu; resolver üretmez |
| `FormatOnly` | ID çözümlendi; gerçeklik ve sahiplik tool'a ertelendi |

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Bu modeli taşıyan reasoning çıktısı
- [../../../CustomerSupportBot.Application/Services/Reasoning/EntityVerifier.md](../../../CustomerSupportBot.Application/Services/Reasoning/EntityVerifier.md) — Bu modeli üreten servis
