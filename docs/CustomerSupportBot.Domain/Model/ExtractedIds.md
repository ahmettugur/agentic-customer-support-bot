# ExtractedIds

**Dosya:** `Model/ExtractedIds.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Kullanıcı mesajından regex ile çıkarılmış **ham entity ID'lerini** tutar. DB doğrulaması henüz yapılmamıştır — sadece format kontrol edilmiştir.

## 2. Hangi Amaçla Kullanılır

`IdExtractor.Extract()` bu modeli üretir. `EntityVerifier` ham ID'leri query/history/authenticated
session önceliğiyle `VerifiedEntities` modeline dönüştürür; order/complaint gerçekliği ve sahipliği
specialist tool'da doğrulanır.

> `ExtractedIds`, tek mesajdaki ham regex sonucudur. `VerifiedEntities`, geçmiş bağlamı ve
> authenticated identity'yi de içeren güvenli resolution sonucudur. Siparişin varlığı/durumu
> ancak tool sonucu ile bilinir.

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

- [VerifiedEntities.md](VerifiedEntities.md) — Güvenli entity resolution sonucu
- [../Services/IdExtractor.md](../Services/IdExtractor.md) — Bu modeli üreten servis
