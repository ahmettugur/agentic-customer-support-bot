# TokenService

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/Auth/TokenService.cs`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.Auth`

## 1. Ne İşe Yarar

⚠️ **Bu dosya artık yalnızca bir yorum bloğundan ibarettir, hiçbir kod içermez.** İçeriği:

```csharp
// Adapters.Persistence/Auth/TokenService.cs
// Kaldırıldı: orchestrasyon TokenPortService (Application katmanı) ve
// JWT imzalama JwtAccessTokenProvider (bu adapter) olarak ikiye ayrıldı.
```

## 2. Hangi Amaçla Kullanıldığı

Hiçbir amaçla — çağrılan/derlenen bir sınıf değildir. Tarihsel bir "burada eskiden ne vardı, şimdi nerede" işaretçisi olarak dosya diskte bırakılmış.

## 3. Sorumlulukları

Yok.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Eski `TokenService`'in sorumlulukları ikiye ayrıldı:
- Orkestrasyon (login akışı, refresh token rotasyonu, hangi sırayla ne çağrılacağı) → `TokenPortService` (Application katmanı).
- JWT imzalama → [JwtAccessTokenProvider](JwtAccessTokenProvider.md) (bu klasör).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Bu, tek sorumluluk ilkesine (SRP) yönelik bir refactor'ün izidir: tek bir "her şeyi yapan" `TokenService` sınıfı, orkestrasyonu (hangi adım ne zaman) framework-bağımsız Application katmanına, imzalama detayını (IdentityModel kütüphanesi) Adapters katmanına ayırarak bölünmüştür.

> ⚠️ **Stajyerler için not:** Bu dosyanın projede durmasının pratik bir değeri yok — silinmesi önerilir, ama bu dokümantasyon geçişinin kapsamı dışında bırakıldı (kod değişikliği, sadece dokümantasyon değil). Projeyi yöneten geliştiriciye danışarak silinebilir.

## 6. Metotlar / Üyeler

Yok — dosyada tanımlı hiçbir tip/üye yok.

## 7. Bağımlılıklar

Yok.
