# CustomerIdentityHintBuilder

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/CustomerIdentityHintBuilder.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`CustomerIdentityHintBuilder`, oturumdaki müşteri kimlik bilgilerini (giriş yapmış müşteri veya serbest sorguda doğrulanmış müşteri) ajanların ve muhakeme modelinin anlayacağı standart Türkçe sistem talimatı ipucuna dönüştüren yardımcı sınıftır.

## Hangi amaçla kullanılır`?

- Kullanıcının `AuthenticatedCustomerId` (JWT login) veya `CustomerId` bilgilerini `"KULLANICI KİMLİK BİLGİSİ: Aktif müşteri ID: 1008. Sipariş veya şikayet sorgularında bu ID'yi kullanın."` şeklinde sistem mesajı formatında üretmek.
- Bilgi yoksa `null` dönerek istemi gereksiz yere uzatmamak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Build`
```csharp
public static string? Build(AgentSession session)
```
- **Ne işe yarar?:** Oturumdaki kimlik alanlarını kontrol eder ve formatlar.
- **İç Mantığı:** `session.State.AuthenticatedCustomerId` veya `session.State.CustomerId` mevcutsa biçimlendirilmiş talimat metnini döner.

## Bağımlılıklar

- [AgentSession](../../../CustomerSupportBot.Domain/Model/AgentSession.md)
