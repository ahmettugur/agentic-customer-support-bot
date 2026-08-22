# CustomerProfileContextProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/CustomerProfileContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`CustomerProfileContextProvider`, doğrulanmış müşterinin ([SessionState.AuthenticatedCustomerId](../../../CustomerSupportBot.Domain/Model/AgentSessionState.md)) profili ve etkileşim sentezi (`CustomerUnderstanding`) varsa, bunu ajanların dikkate alması için `"## 👤 Müşteri Profili"` formatında dinamik sistem bağlamına enjekte eden sağlayıcıdır.

## Hangi amaçla kullanılır`?

- Giriş yapmış müşterinin personasını, admin notlarını, tercih ettiği dil/ton ayarlarını, sıkça tekrarladığı niyetleri (`TopIntents`) ve çıkarılan davranışsal özelliklerini (`Traits`) ajan istemine aktarmak.
- **Güvenlik Kısıtı:** Kullanıcının serbest metinden söylediği geçici `SessionState.CustomerId` yerine, **sadece JWT ile doğrulanmış** `AuthenticatedCustomerId` üzerinden profil yükleyerek başkasının verilerinin sızdırılmasını (IDOR) engellemek.

## Sorumlulukları

- **Üstlendiği:**
  - `Name = "CustomerProfile"` ve `Order = 6` (Uzun vadeli tercihler semantik RAG'den önce gelmelidir).
  - `_understanding.Build(session)` ile profil sentezini alıp markdown tablosu/listesi formatına dönüştürmek.

## Constructor ve Başlatma Mantığı

```csharp
public CustomerProfileContextProvider(
    ICustomerUnderstandingService understanding,
    ILogger<CustomerProfileContextProvider> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_understanding`: Müşteri verilerini birleştiren profil servis arayüzü (`ICustomerUnderstandingService`).
- `_logger`: Günlükleme motoru atanır.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetContextAsync`
```csharp
public Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Müşteri profili markdown bloğunu üretir; profil yoksa `null` döner.
- **İç Mantığı:**
  1. `_understanding.Build(session)` çağrılır.
  2. Sonuç `null` ise hemen `null` döner.
  3. `StringBuilder` ile ID, Persona, Admin notu, Tercih edilen ton/dil, Toplam oturum/tur sayısı, Sık niyetler ve Çıkarılan özellikler (`Traits`) satır satır formatlanır.

## Özellikler/Properties

- `Name` (`string`): Sabit `"CustomerProfile"`.
- `Order` (`int`): `6`.

## Bağımlılıklar

- [IContextProvider](IContextProvider.md)
- `CustomerSupportBot.Application.Ports.Outbound.ICustomerUnderstandingService`
- [CustomerUnderstanding](../../../CustomerSupportBot.Domain/Model/Memory/CustomerUnderstanding.md)
