# ExtractedIds

**Dosya:** `Model/ExtractedIds.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Kullanıcı mesajından regex ile çıkarılmış **ham entity ID'lerini** tutar. DB doğrulaması henüz yapılmamıştır — sadece format kontrol edilmiştir.

## 2. Hangi Amaçla Kullanılır

`IdExtractor.Extract()` bu modeli üretir. `EntityVerifier` bu ham ID'leri alıp DB'de doğrulayarak `VerifiedEntities`'e dönüştürür.

> 💡 **Analiz notu:** `ExtractedIds` = "1030 yazılmış ama gerçekten sipariş mi bilmiyoruz" (ham çıkarım). `VerifiedEntities` = "1030 DB'de var, Dell XPS 15 siparişi, kargolanmış" (doğrulanmış sonuç). İkisi arasındaki fark güvenilirlik farkıdır.

## 3. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `OrderId` | `string?` | Çıkarılan sipariş ID |
| `CustomerId` | `string?` | Çıkarılan müşteri ID |
| `ComplaintId` | `string?` | Çıkarılan şikayet ID |
| `IsCustomerIdAssumed` | `bool` | Bağlam kelimesi olmadan varsayılan mı? (zayıf sinyal) |
| `HasAny` | `bool` | **Computed** — herhangi bir ID var mı? |

> ⚠️ `IsCustomerIdAssumed=true` ise CustomerId zayıf bir varsayımdır — kısa sorguda tek sayı "müşteri ID" olarak varsayılmış ama gerçek bağlam sinyali yok.

## Bağlantılar

- [VerifiedEntities.md](VerifiedEntities.md) — Doğrulanmış sonuç
- [../Services/IdExtractor.md](../Services/IdExtractor.md) — Bu modeli üreten servis
