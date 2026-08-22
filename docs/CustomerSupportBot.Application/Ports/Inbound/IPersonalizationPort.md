# IPersonalizationPort

**Dosya:** `Ports/Inbound/IPersonalizationPort.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Müşteri profillerinin (uzun ömürlü, LLM tarafından türetilmiş kişiselleştirme verisi) admin panelinden yönetimi için primary port.

## 2. Hangi amaçla kullanılır?

Admin panelinin "müşteri profilleri" sayfası, profilleri listelemek, tek bir profili görmek, admin notu eklemek/silmek ve profili manuel olarak yeniden hesaplatmak (`RefreshProfileAsync`) için kullanır.

## 3. Sorumlulukları

- **Üstlendiği:** Profil CRUD'unun admin tarafı.
- **Üstlenmediği:** Profilin runtime'da (chat sırasında) nasıl okunduğu/güncellendiği — bu `CustomerProfileContextProvider`/`Services/Personalization` içindeki arka plan sürecinin işidir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `Services/Personalization` altında.
- Admin panelindeki müşteri profilleri sayfası tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`SetAdminNote`, LLM'in ürettiği çıkarımların üzerine admin'in manuel bir not ekleyebilmesini sağlar — otomatik kişiselleştirmenin yanlış/eksik olduğu durumlarda insan düzeltmesi için bir kaçış kapısıdır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `(int Count, IReadOnlyList<CustomerProfile> Items) GetProfiles(int take = 100)` | Profilleri (toplam sayıyla birlikte) listeler. |
| `CustomerProfile? GetProfile(string customerId)` | Tek müşteri profili. |
| `Task<CustomerProfile?> RefreshProfileAsync(string customerId, CancellationToken ct = default)` | Profili manuel olarak yeniden hesaplatır. |
| `CustomerProfile SetAdminNote(string customerId, string? note)` | Admin notu ekler/günceller/temizler. |
| `bool DeleteProfile(string customerId)` | Profili siler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.Memory.CustomerProfile`.

## Bağlantılar

- [CustomerProfile](../../Domain/Model/Memory/CustomerProfile.md)
