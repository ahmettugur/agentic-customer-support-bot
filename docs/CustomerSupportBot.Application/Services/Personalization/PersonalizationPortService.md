# PersonalizationPortService

- **Kaynak:** `Services/Personalization/PersonalizationPortService.cs`
- **Tür:** `public sealed class : IPersonalizationPort`
- **Namespace:** `CustomerSupportBot.Application.Services.Personalization`

## 1. Ne İşe Yarar

`IPersonalizationPort` (Inbound/Driving port) implementasyonu — Api katmanının admin panel
uçlarının müşteri profillerini okuma/düzenleme/silme ve yeniden özetletme (LLM ile) için
kullandığı tek giriş noktası.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin endpoint'leri (`/admin/profiles` gibi) doğrudan `CustomerProfileService`
veya `ICustomerProfileStore`'a bağımlı olmasın diye araya konan ince bir port implementasyonu.
Kendi iş mantığı neredeyse yok — çağrıları doğru alt bileşene yönlendirir (thin adapter /
delegating port pattern).

## 3. Sorumlulukları

**Üstlendiği:** `IPersonalizationPort` sözleşmesindeki 5 metodu, `CustomerProfileService`
(yazma/consolidate) ve `ICustomerProfileStore`'a (okuma/silme) delege ederek karşılamak.

**Üstlenmediği:** Profil güncelleme kurallarının kendisi ([`CustomerProfileService`](CustomerProfileService.md)'in
işi) veya profilin nasıl saklandığı (`ICustomerProfileStore`'un işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Api katmanındaki admin endpoint'leri tarafından çağrılır (Inbound port).
- [`CustomerProfileService`](CustomerProfileService.md) — `RefreshProfileAsync` bunun
  `ConsolidateAsync`'ine delege eder.
- `ICustomerProfileStore` (Outbound port) — `GetProfiles`/`GetProfile`/`SetAdminNote`/`DeleteProfile`
  doğrudan bu store üzerinden çalışır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride Api katmanının Application katmanının iç servislerine (`CustomerProfileService`)
değil, portlara (`IPersonalizationPort`) bağımlı olması gerekir — bu sınıf o sözleşmeyi
gerçekleştiren "driving adapter"dır. İçinde iş kuralı yoktur, bilerek: kurallar zaten
`CustomerProfileService` içinde toplanmış durumda, burada tekrarlanmaz (DRY).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetProfiles(take = 100)` | `_profiles.Count` ve `_profiles.List(take)`'i birlikte döner — admin panelindeki profil listesi sayfası için. |
| `GetProfile(customerId)` | Tek bir müşterinin profilini döner, yoksa `null`. |
| `RefreshProfileAsync(customerId, ct)` | `CustomerProfileService.ConsolidateAsync`'e delege eder — admin panelden "profili yeniden özetle" butonunun arkasındaki çağrı. |
| `SetAdminNote(customerId, note)` | Profile serbest metin bir admin notu ekler/temizler (`note` boşsa `null`'a çevrilir); profil yoksa `GetOrCreate` ile oluşturulur. |
| `DeleteProfile(customerId)` | Profili store'dan siler, başarılıysa `true` döner. |

## 7. Bağımlılıklar

Constructor injection ile: `CustomerProfileService`, `ICustomerProfileStore`.

## Bağlantılar

- [CustomerProfileService.md](CustomerProfileService.md) — asıl iş mantığı burada
