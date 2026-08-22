# A2ASubjectIdentity

**Dosya:** `Services/A2A/A2ASubjectIdentity.cs`
**Tür:** `public static class` (saf, durumsuz yardımcı sınıf)
**Namespace:** `CustomerSupportBot.Application.Services.A2A`

## 1. Ne İşe Yarar

A2A (Agent-to-Agent) kanalında üretilen **özne token'ının** (`sub` claim) kimlik biçimini
kuran (`BuildId`) ve çözen (`TryGetPartnerId`) **tek yer**. Biçim sabit: `a2a:{partnerId}:{customerId}`.

## 2. Hangi Amaçla Kullanılır

Bu biçim iki farklı yerde tüketilir:

1. **Token üretimi** — [`A2ATokenExchangeService`](A2ATokenExchangeService.md), bir partner
   belirli bir müşteri adına hareket etme yetkisi aldığında `BuildId` ile bu kimliği üretip
   JWT'nin `sub` claim'ine yazar.
2. **Rate limit bölümlemesi** — partner başına istek sınırlaması uygulanırken, gelen token'ın
   `sub` alanından `TryGetPartnerId` ile partner kimliği geri çıkarılır ve rate limiter bu
   partnere özel bir "bucket" kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Biçimi kurmak/çözmek — sadece string birleştirme/ayrıştırma.
- **Üstlenmediği:** Yetkilendirme kararı vermek (bu [`ConfiguredA2ASubjectAuthorizer`](ConfiguredA2ASubjectAuthorizer.md)'da),
  token imzalamak/doğrulamak (bu `IJwtAccessTokenProvider`'da).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [`A2ATokenExchangeService`](A2ATokenExchangeService.md) tarafından token üretilirken çağrılır.
- Partner bazlı rate-limit partition key'i türetilirken (Api katmanında, A2A endpoint'lerinin
  rate limiter yapılandırmasında) çağrılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden tek yerde toplandı:** Bu biçim ("a2a:partnerId:customerId") iki bağımsız yerde
> kullanılıyor. Biri token *üretirken*, diğeri rate limiter partner'ı token'dan *geri çıkarırken*.
> İki taraf ayrı ayrı elle string birleştirseydi/ayrıştırsaydı, biri (örn. ayraç karakteri veya
> alan sırası) değişince diğeri fark etmeden bozulurdu — rate limiter partner'ı yanlış (veya hiç)
> çıkaramaz, tüm partnerler tek bir "bilinmeyen" anahtarına düşer ve **partner başına sınır
> sessizce ortadan kalkar**. Bu, görünür bir hata (exception, 500) değil, sessizce kaybolan bir
> güvenlik kontrolü olurdu — bu yüzden biçim TEK sınıfta, iki taraf da aynı sınıfı çağırıyor.

`TryGetPartnerId`, biçim beklenenden farklıysa (`null`/boş girdi, `"a2a:"` prefix'i yok, parça
sayısı 3'ten az) **exception fırlatmak yerine `null` döner** — çağıran taraf bunu "bilinmeyen
partner" olarak ele almalı, asla tahmin etmemelidir (örn. ilk parçayı partner sanmak gibi).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `BuildId(string partnerId, string customerId): string` | `"a2a:{partnerId}:{customerId}"` biçiminde kimlik üretir. |
| `TryGetPartnerId(string? subjectId): string?` | Kimlikten partner'ı çıkarır; biçim/prefix uymuyorsa veya partner alanı boşsa `null` döner. |

Sabitler: `Prefix = "a2a"`, `Separator = ':'` — biçim değişecekse tek değişiklik noktası buradadır.

## 7. Bağımlılıklar

Yok — saf, durumsuz statik yardımcı sınıf. Hiçbir servis inject etmez/edilmez.

## Bağlantılar

- [A2ATokenExchangeService.md](A2ATokenExchangeService.md) — bu kimliği üreten taraf
- [ConfiguredA2ASubjectAuthorizer.md](ConfiguredA2ASubjectAuthorizer.md) — yetki kararını veren taraf
