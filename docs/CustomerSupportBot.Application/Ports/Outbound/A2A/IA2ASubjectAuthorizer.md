# IA2ASubjectAuthorizer

**Kaynak:** `Ports/Outbound/A2A/IA2ASubjectAuthorizer.cs`
**Implementasyon:** [`ConfiguredA2ASubjectAuthorizer`](../../../Services/A2A/ConfiguredA2ASubjectAuthorizer.md) (Application katmanında — bu, dış bir sisteme çıkmayan, konfigürasyon-tabanlı bir karar olduğu için istisnai olarak Adapters değil Application'da implemente edilir)

## 1. Ne İşe Yarar

A2A (Agent-to-Agent) token değişiminde sorulan tek soruyu yanıtlar: **partner P, müşteri C
adına hareket edebilir mi?** `CanActForCustomerAsync(partnerId, customerId, ct)` bir `bool`
döner; belirsizlik durumunda implementasyon `false` dönmelidir.

## 2. Hangi Amaçla Kullanılır

Bir partner sistemi (örn. bir mobil uygulama backend'i), kendi adına değil bir müşteri adına
chat/tool çağrısı yapmak istediğinde A2A token değişimi sırasında çağrılır. Sonuç `false` ise
token üretilmez, istek reddedilir.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca "bu partner + bu müşteri" ikilisinin yetkili olup olmadığına karar
  vermek.
- **Üstlenmediği:** Token üretimi/doğrulaması (bkz. [`IJwtAccessTokenProvider`](../Auth/IJwtAccessTokenProvider.md)),
  tool seviyesinde sahiplik kontrolü (bkz. [`IOrderToolsService`](../IOrderToolsService.md)
  içindeki `customerId` parametreleri) — bu port yalnızca A2A kanalındaki İLK yetki kapısıdır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- A2A token değişim akışının neresinde çağrıldığını görmek için bkz.
  [`../../../../CustomerSupportBot.Api/A2A.md`](../../../../CustomerSupportBot.Api/A2A.md).
- Gerçek implementasyon `ConfiguredA2ASubjectAuthorizer` — konfigürasyon tabanlı statik bir
  eşleme kullanır (örn. appsettings'te partner→izinli müşteri listesi), dış bir yetki
  sunucusuna gitmez.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu bir teknik kural değil **iş kuralıdır** — "hangi partner hangi müşteriye erişebilir"
sorusunun cevabı işletmeye göre değişir (partnerin kendi tanıttığı müşteriler, bir sözleşme
kapsamı, bir bayi hiyerarşisi olabilir). Bu kararı token değişimi mantığının içine gömmek onu
ileride değiştirilemez hâle getirirdi; bu yüzden ayrı bir port.

> ⚠️ **Bu kontrol atlanırsa ne olur:** partner token'ı ele geçiren biri, herhangi bir müşteri
> numarasını isteyerek o müşterinin sipariş geçmişine erişebilir. Bu port A2A kanalındaki
> **tek müşteri-bazlı yetki sınırıdır**; tool katmanındaki sahiplik kontrolü ondan sonra gelir
> ve onun yerini TUTMAZ — o yalnızca "token'daki müşteriye ait mi" sorusunu sorar, token'ın
> doğru müşteriye ait olduğunu zaten varsayar.

## 6. Metotlar

| Metot | Açıklama |
|---|---|
| `Task<bool> CanActForCustomerAsync(string partnerId, string customerId, CancellationToken ct = default)` | Partner'ın müşteri adına hareket etme izni var mı? Belirsizlikte `false`. |

## 7. Bağımlılıklar

Port arayüzünün kendisi bağımlılıksızdır (yalnızca `System.Threading.Tasks`). İmplementasyonun
bağımlılıkları için `ConfiguredA2ASubjectAuthorizer.md`'ye bakın.
