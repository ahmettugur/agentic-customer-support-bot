# ICustomerProfileService

**Kaynak:** `Ports/Outbound/ICustomerProfileService.cs`
**Implementasyon:** [`CustomerProfileService`](../../Services/Personalization/CustomerProfileService.md)

## 1. Ne İşe Yarar

Müşteri profil güncelleme port'u — adapter'ların bir etkileşim sonrası profil kaydı yazması
için.

## 2. Hangi Amaçla Kullanılır

Her tur bittiğinde `WorkflowRunner`/`ChatPortService` bu port'u `RecordInteractionAsync` ile
çağırarak müşteri profiline (varsa) yeni etkileşimi işler.

## 3. Sorumlulukları

- **Üstlendiği:** Bir etkileşimi (sorgu, cevap, intent, opsiyonel rating) profile işlemek.
- **Üstlenmediği:** Profilin depolanması ([`ICustomerProfileStore`](Persistence/ICustomerProfileStore.md)'un
  işi) veya profilin başka verilerle sentezlenmesi
  ([`ICustomerUnderstandingService`](ICustomerUnderstandingService.md)'in işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Personalization/CustomerProfileService` implemente eder;
[`ICustomerProfileStore`](Persistence/ICustomerProfileStore.md)'u inject eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`customerId` nullable'dır çünkü anonim/login'siz turlarda profil güncellenemez — çağıran taraf
bu durumu kontrol etmek zorunda kalmadan güvenle çağırabilir, servis içeride no-op davranır.
`isNewSession` parametresi, "ilk temas" metriklerinin (örn. yeni müşteri sayısı) doğru
sayılabilmesi için ayrıca taşınır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<CustomerProfile?> RecordInteractionAsync(string? customerId, string userQuery, string botResponse, string? intent, int? rating = null, bool isNewSession = false, CancellationToken ct = default)` | Etkileşimi profile işler; `customerId` yoksa `null` döner. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Memory.CustomerProfile`'a bağımlıdır.
