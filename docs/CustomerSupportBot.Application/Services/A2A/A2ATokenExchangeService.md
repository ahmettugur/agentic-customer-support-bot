# A2ATokenExchangeService

**Dosya:** `Services/A2A/A2ATokenExchangeService.cs`
**Tür:** `public sealed class` + yardımcı tipler (`A2ARoles` statik sınıf, `A2ATokenResult` record)
**Namespace:** `CustomerSupportBot.Application.Services.A2A`

## 1. Ne İşe Yarar

Bir **partner** (dış sistem, kendi makine kimliğiyle kimlik doğrulanmış) belirli bir **müşteri**
adına A2A ajanlarını çağırmak istediğinde, partner'ın kendi token'ını doğrudan kullanmasına izin
vermek yerine, o partner+müşteri çifti için **kısa ömürlü, tek müşteriye kilitli bir "özne
token'ı"** üretir (OAuth 2.0 Token Exchange / RFC 8693 deseni).

## 2. Hangi Amaçla Kullanılır

A2A akışı iki aşamalıdır:
1. Partner kendi makine kimlik bilgileriyle giriş yapar → `Partner` rolünde bir token alır.
2. Partner, belirli bir müşteri adına işlem yapmak istediğinde bu partner-token'ı ile
   `ExchangeAsync`'i çağırır → yetkiliyse, `Subject` rolünde, `sub` claim'i
   `A2ASubjectIdentity.BuildId(partnerId, customerId)` olan yeni bir token alır. Bundan sonraki
   tüm A2A çağrıları bu özne token'ı ile yapılır.

## 3. Sorumlulukları

- **Üstlendiği:** Yetki kontrolünü ([`IA2ASubjectAuthorizer`](../../Ports/Outbound/A2A/README.md)'a
  sorarak) tetiklemek, yetki varsa `IJwtAccessTokenProvider` ile imzalı token üretmek, üretimi loglamak.
- **Üstlenmediği:** Yetki kararının KENDİSİ (bu iş kuralı [`ConfiguredA2ASubjectAuthorizer`](ConfiguredA2ASubjectAuthorizer.md)'da),
  token'ın imzalanma/doğrulanma mekaniği (`IJwtAccessTokenProvider`, Adapters katmanında).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Inject eder:** `IA2ASubjectAuthorizer` (yetki kararı), `IJwtAccessTokenProvider` (token üretimi),
  `IOptions<A2AOptions>` (özne token ömrü), `ILogger`, `TimeProvider` (test edilebilirlik için).
- **Kimin tarafından çağrılır:** Api katmanındaki A2A token-exchange endpoint'i.
- [`A2ASubjectIdentity`](A2ASubjectIdentity.md)'yi kullanarak `sub` claim'ini kurar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden partner token'ı A2A çağrılarında doğrudan kullanılmıyor:** Partner token'ı "hangi
> sistem konuşuyor" sorusunu cevaplar, "hangi müşteri adına" sorusunu cevaplamaz. Doğrudan
> kullanılsaydı, müşteri kimliğinin her çağrının gövdesinde bir **parametre** olarak taşınması
> gerekirdi — yani tool'ların güvendiği kimlik, istemcinin serbestçe değiştirebildiği bir alan
> olurdu. Bu tam olarak sohbet kanalında kapatılan güvenlik açığının (LLM'e serbest metinden
> `customerId` sorduramama, bunun yerine `SessionState.AuthenticatedCustomerId`'yi kullanma)
> A2A tarafında yeniden açılması anlamına gelirdi. Değişim sonrası müşteri kimliği artık
> **imzalı token'ın içinde** geliyor — çağıran taraf değiştiremez.

Üretilen `UserInfo` nesnesi (subject) **kalıcı bir kullanıcı kaydı değildir** — sadece token'ın
taşıyacağı iddiaların (claims) geçici bir kabıdır; `Id` alanına hem partner hem müşteri kimliği
gömülür ki denetim kaydında ("audit log") "bu token hangi partner adına, hangi müşteri için
üretildi" sorusu cevaplanabilsin.

`ExchangeAsync` yetkisiz durumda **neden fırlatmadan sessizce `null` döner**: çağıran (Api
endpoint'i) bunu 403'e çevirmeli ve **sebebi istemciye açmamalıdır** — "bu müşteri yok" ile "bu
müşteriye yetkin yok" cevapları arasındaki fark, dışarıdan müşteri numarası taramasını (enumeration
attack) kolaylaştırır.

`A2ARoles` sınıfı `Partner` ve `Subject` rollerini ayrı tutar; bilinçli bir izolasyon: sohbet/sesli
kanalın kullandığı `"Customer"` rolü A2A'da geçersizdir, A2A'nın `Subject` rolü de sohbet kanalında
geçersizdir — bir kanalın token'ı diğer kanalda asla kullanılamaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `A2ARoles.Partner` (`const string = "Partner"`) | Partner makine kimliğinin rolü. |
| `A2ARoles.Subject` (`const string = "A2ASubject"`) | Değişimle üretilen, tek müşteriye kilitli özne token'ının rolü. |
| `A2ATokenResult(string AccessToken, DateTime ExpiresAt, string CustomerId)` | Değişim sonucu — üretilen token, son kullanma zamanı ve hangi müşteri için üretildiği. |
| `ExchangeAsync(string partnerId, string customerId, CancellationToken ct = default): Task<A2ATokenResult?>` | Yetki kontrolü yapar, geçerse özne token'ı üretir; yetkisizse veya girdi boşsa `null` döner. |

## 7. Bağımlılıklar (Constructor Injection)

- `IA2ASubjectAuthorizer` — yetki kararı.
- `IJwtAccessTokenProvider` — imzalı access token üretimi.
- `IOptions<A2AOptions>` — `SubjectTokenMinutes` (varsayılan 5dk, kısa tutulur çünkü tek bir çağrı için yeterli).
- `ILogger<A2ATokenExchangeService>` — üretim/red loglaması.
- `TimeProvider?` (opsiyonel, varsayılan `TimeProvider.System`) — test edilebilir zaman kaynağı.

## Bağlantılar

- [A2ASubjectIdentity.md](A2ASubjectIdentity.md) — `sub` claim biçimi
- [ConfiguredA2ASubjectAuthorizer.md](ConfiguredA2ASubjectAuthorizer.md) — yetki kararı ve `A2AOptions`
