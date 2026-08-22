# IRefreshTokenRepository

**Kaynak:** `Ports/Outbound/Auth/IRefreshTokenRepository.cs`
**Implementasyon:** [`EfRefreshTokenRepository`](../../../../CustomerSupportBot.Adapters.Persistence/EfCore/Auth/EfRefreshTokenRepository.md)

## 1. Ne İşe Yarar

Refresh token kalıcılığı için secondary port: oluşturma, hash ile bulma, iptal etme
(koşulsuz ve koşullu iki farklı metotla).

## 2. Hangi Amaçla Kullanılır

`TokenPortService.RefreshAsync` akışında: gelen refresh token hash'lenip `FindByHashAsync` ile
bulunur, geçerliyse eski token `TryRevokeAsync` ile iptal edilip yeni bir token üretilir
(refresh token rotation).

## 3. Sorumlulukları

- **Üstlendiği:** Refresh token kaydının CRUD'u ve iptal mantığı.
- **Üstlenmediği:** JWT access token üretimi ([`IJwtAccessTokenProvider`](IJwtAccessTokenProvider.md)'ın işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/EfCore/Auth/EfRefreshTokenRepository` implemente eder; `TryRevokeAsync`
EF Core `ExecuteUpdateAsync` ile tek bir koşullu `UPDATE ... WHERE Id = id AND RevokedAt IS NULL`
çalıştırır (bu yüzden **EF InMemory sağlayıcısında çalışmaz** — testler gerçek Postgres
(`PostgresCatalogFixture`) veya `InMemoryRefreshTokenRepository` test fake'i kullanmalıdır).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`TryRevokeAsync` neden `RevokeAsync`'in yanında ayrıca var:** `RevokeAsync` oku-değiştir-yaz
> döngüsüdür ve eşzamanlılığa karşı korumasızdır. Aynı refresh token'la eşzamanlı gelen iki
> yenileme isteği ikisi de "hâlâ geçerli" okuyup ikisi de yeni bir token üretebilirdi; tek bir
> çalıntı/paylaşılan token'dan iki geçerli oturum zinciri doğar ve yeniden kullanım tespiti
> sessizce atlanırdı. `TryRevokeAsync` tek bir koşullu `UPDATE`'tir — yalnızca BİR çağıran
> satırı gerçekten değiştirir; kaybeden `false` alır ve `TokenPortService` bu isteği reddeder.
> Bu, projede tekrarlanan bir "koşullu sahiplenme" desenidir (bkz. HITL onay kararı claiming'i
> ile aynı prensip).

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task CreateAsync(string id, string userId, string tokenHash, DateTime expiresAt, DateTime createdAt, CancellationToken ct = default)` | Yeni refresh token kaydı oluşturur. |
| `Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct = default)` | Hash ile kaydı bulur. |
| `Task RevokeAsync(string id, DateTime revokedAt, string? replacedByTokenHash, CancellationToken ct = default)` | Koşulsuz iptal — oku-değiştir-yaz. |
| `Task<bool> TryRevokeAsync(string id, DateTime revokedAt, string? replacedByTokenHash, CancellationToken ct = default)` | **Koşullu** iptal: yalnızca kayıt HÂLÂ iptal edilmemişse iptal eder (tek UPDATE, race-safe). Kayıt zaten iptal edilmişse `false` döner. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Auth.RefreshTokenInfo`'ya bağımlıdır.
