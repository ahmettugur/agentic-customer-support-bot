# VerifiedEntities

**Dosya:** `Model/VerifiedEntities.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `VerifiedEntity`, `EntitySource` enum, `EntityVerification` enum (aynı dosyada)

## 1. Ne İşe Yarar

`EntityVerifier`'ın query + history + session state + DB birleştirerek ürettiği **doğrulanmış entity sonucu**dur. Reasoning prompt'una enjekte edilerek LLM'in "zaten bilinen bilgi için tekrar soru sormasını" önler.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.VerifiedEntities` olarak taşınır. `BuildPromptBlock()` metodu ile LLM'in okuyacağı "[VERIFIED ENTITIES]" bloğuna dönüştürülür. Workflow'da tek doğruluk kaynağıdır.

> 💡 **Analiz notu:** Hastane kayıt sistemi gibi düşün — "TC 12345 → DB'de var, Ahmet Yılmaz, son vizite: 3 gün önce." LLM'e bu bilgi verildiğinde "TC numaranız nedir?" diye tekrar sormaz. Hallucination riskini düşürür.

## 3. Metotlar / Üyeler

### VerifiedEntities

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `OrderId` | `VerifiedEntity?` | Doğrulanmış sipariş |
| `CustomerId` | `VerifiedEntity?` | Doğrulanmış müşteri |
| `ComplaintId` | `VerifiedEntity?` | Doğrulanmış şikayet |
| `DerivedLastOrderId` | `string?` | Müşterinin en son siparişi (türetilmiş) |
| `DerivedOrderCount` | `int?` | Müşterinin toplam sipariş sayısı (türetilmiş) |
| `HasAny` | `bool` | **Computed** — doğrulanmış entity var mı? |
| `HasAnyVerified` | `bool` | **Computed** — DB-verified entity var mı? |

### VerifiedEntity

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Value` | `string` | Entity değeri (ör. "1030") |
| `Source` | `EntitySource` | Nereden çıkarıldı |
| `Verification` | `EntityVerification` | Doğrulama seviyesi |
| `Attributes` | `Dictionary<string, string>?` | DB'den gelen özet bilgiler |

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
| `Verified` | Format doğru + DB'de mevcut |
| `NotFoundInDb` | Format doğru ama DB'de yok (kullanıcı yanlış numara vermiş olabilir) |
| `FormatOnly` | Sadece format doğrulanmış, DB kontrolü yapılmamış |

## Bağlantılar

- [ExtractedIds.md](ExtractedIds.md) — Ham çıkarım (doğrulama öncesi)
- [ReasoningResult.md](ReasoningResult.md) — Bu modeli taşıyan reasoning çıktısı
- [../../CustomerSupportBot.Application/ReasoningPipeline.md](../../CustomerSupportBot.Application/Services/Reasoning/ReasoningService.md) — EntityVerifier pipeline katmanı
