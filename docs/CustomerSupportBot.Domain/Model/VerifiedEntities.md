# VerifiedEntities

**Dosya:** `Model/VerifiedEntities.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `VerifiedEntity`, `EntitySource` enum, `EntityVerification` enum (aynı dosyada)

## 1. Ne İşe Yarar

`EntityVerifier`ın query + history + authenticated session kaynaklarını birleştirerek ürettiği
**çözümlenmiş entity sonucu**dur. Reasoning prompt'una enjekte edilerek LLM'in kullanıcıdan zaten
sağladığı ID'yi tekrar istemesini önler. Sipariş/şikayet gerçekliği ve sahipliği bu modelde değil,
specialist tool sonucunda doğrulanır.

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
| `OrderId` | `VerifiedEntity?` | Çözümlenmiş sipariş adayı; tool'da doğrulanır |
| `CustomerId` | `VerifiedEntity?` | Authenticated session müşteri kimliği |
| `ComplaintId` | `VerifiedEntity?` | Çözümlenmiş şikayet adayı; tool'da doğrulanır |
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
| `Query` | Güncel kullanıcı mesajından |
| `History` | Önceki konuşma turundan |
| `SessionState` | Oturum durumundan (AuthenticatedCustomerId) |
| `Derived` | Başka entity'den türetilmiş |

### EntityVerification Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Verified` | Güvenilir sistem kaynağı (bugün authenticated customer identity) |
| `NotFoundInDb` | Geriye dönük/harici doğrulayıcı sonucu; resolver üretmez |
| `FormatOnly` | ID çözümlendi; gerçeklik ve sahiplik tool'a ertelendi |

## Bağlantılar

- [ExtractedIds.md](ExtractedIds.md) — Ham çıkarım (doğrulama öncesi)
- [ReasoningResult.md](ReasoningResult.md) — Bu modeli taşıyan reasoning çıktısı
- [../../CustomerSupportBot.Application/ReasoningPipeline.md](../../CustomerSupportBot.Application/Services/Reasoning/ReasoningService.md) — EntityVerifier pipeline katmanı
