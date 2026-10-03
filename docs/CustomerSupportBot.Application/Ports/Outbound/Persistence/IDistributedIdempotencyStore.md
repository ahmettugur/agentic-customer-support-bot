# IDistributedIdempotencyStore

**Kaynak:** `Ports/Outbound/Persistence/IDistributedIdempotencyStore.cs`
**İmplementasyon:** [`RedisIdempotencyStore`](../../../../CustomerSupportBot.Adapters.Redis/Idempotency/RedisIdempotencyStore.md)

## 1. Ne İşe Yarar

Yan etkili tool çağrılarının (sipariş oluşturma, şikayet kaydı) mükerrer tespit kaydını pod'lar
arasında paylaşan secondary port.

## 2. Hangi Amaçla Kullanılır

[`SideEffectIdempotencyCache`](../../../Services/Tools/SideEffectIdempotencyCache.md)'in ikinci
katmanıdır: yerel (pod içi) cache ıskaladığında burada aynı imzalı bir çağrının kaydı aranır;
başarılı bir çağrı sonrası kayıt buraya da yazılır.

## 3. Sorumlulukları

- **Üstlendiği:** Anahtar → oluşturulan kayıt kimliği eşlemesini süreli (TTL) tutmak; ilk yazanın
  kazanması.
- **Üstlenmediği:** Anahtarın nasıl üretildiği (parametre imzası) ve kaydın ne zaman
  sorgulanacağı — bunlar `SideEffectIdempotencyCache`'in işidir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Application katmanında tanımlıdır; Redis adaptörü uygular. Kayıt isteğe bağlıdır: DI'da yoksa
(testler, tek süreç) yalnızca yerel cache çalışır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bellek içi cache pod başına olduğu için, aynı isteğin ikinci denemesi başka bir pod'a düştüğünde
mükerrer kayıt oluşuyordu. Arayüz bilinçli olarak **senkron**dur: tool'lar senkron çalışır ve
StackExchange.Redis'in senkron API'si sync-over-async değil, kütüphanenin birinci sınıf yoludur.

`Set` "ilk yazan kazanır" (NX) semantiğindedir: ilk oluşturulan kayıt kanoniktir, sonraki bir
yazma onun kimliğini değiştirmemeli.

Hata durumunda çağıran (fail-open) yalnızca yerel katmanla devam eder — bu en iyi çaba bir
korumadır, kesin tekillik garantisi değildir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `string? Get(string key)` | Anahtar için kayıtlı kimlik; yoksa `null`. |
| `void Set(string key, string entityId, TimeSpan ttl)` | Kaydı TTL ile yazar; anahtar zaten varsa dokunmaz (NX). |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [SideEffectIdempotencyCache](../../../Services/Tools/SideEffectIdempotencyCache.md)
- [RedisIdempotencyStore](../../../../CustomerSupportBot.Adapters.Redis/Idempotency/RedisIdempotencyStore.md)
