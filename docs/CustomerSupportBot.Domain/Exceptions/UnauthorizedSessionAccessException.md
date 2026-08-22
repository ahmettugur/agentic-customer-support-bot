# UnauthorizedSessionAccessException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/DomainException.cs` (aynı dosyada diğer exception'larla birlikte tanımlı)
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## 1. Ne İşe Yarar

Bir oturuma, o oturumun sahibi olmayan bir müşteri adına erişilmeye çalışıldığında fırlatılır.
`Code = "SESSION_FORBIDDEN"` sabittir.

## 2. Hangi Amaçla Kullanılır

`sessionId` her zaman istemciden gelir (URL ya da istek gövdesi). Kimlik doğrulama (JWT) "bu
kişi bir müşteri mi" sorusunu yanıtlar, "bu oturum onun mu" sorusunu değil — ikincisini
`SessionIdentityBinder` yanıtlar ve ihlal tespit edildiğinde bu exception fırlatılır. Böylece bir
müşterinin başka bir müşterinin `sessionId`'sini tahmin edip/deneyip onun sohbet geçmişini veya
onay bildirimlerini okumasının önüne geçilir.

## 3. Sorumlulukları

- ✅ "Bu session, bu kullanıcıya ait değil" durumunu tip güvenli biçimde taşımak
- ✅ İhlali yapan `SessionId`'yi audit/log amacıyla saklamak
- ❌ Kimliği doğrulamak (authentication) — bu JWT middleware'inin işi
- ❌ Yetkilendirme kararını vermek — bu `SessionIdentityBinder`'ın işi, exception sadece sonucu taşır

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim fırlatır:** `SessionIdentityBinder` (Application katmanı) — session sahipliği kontrolü ihlal edildiğinde
- **Kim yakalar:** API katmanındaki global exception handler (`DomainExceptionHandler`) — HTTP 403'e çevirir

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 **`SessionId` her zaman istemciden gelir, güvenilmez bir girdidir.** Bu exception'ın varlığı,
> "kimliği doğrulanmış olmak" ile "bu kaynağa erişim yetkisi olmak"ın iki ayrı kontrol olduğunu
> kod düzeyinde somutlaştırır — biri eksik olsa diğeri onu telafi etmez.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `SessionId` | `string` | Erişilmeye çalışılan, çağıranın sahibi olmadığı oturum kimliği |
| `Code` | `string` | Kalıtım yoluyla `DomainException`'dan gelir, sabit değeri `"SESSION_FORBIDDEN"` |

Kurucu: `UnauthorizedSessionAccessException(string sessionId)`.

## 7. Bağımlılıklar

- [DomainException.md](DomainException.md) — üst sınıf

## Bağlantılar

- [../Model/AgentSession.md](../Model/AgentSession.md) — Sahiplik denetimine konu olan oturum modeli
