# EfRefreshTokenRepository

**Dosya:** `EfCore/Auth/EfRefreshTokenRepository.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore.Auth`
**Port:** [`IRefreshTokenRepository`](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.md)

## 1. Ne İşe Yarar

JWT yenileme (refresh) token'larının veritabanı karşılığı: oluşturma, hash ile arama, ve **iptal etme** — ikinci bir atomik/koşullu iptal metodu ile birlikte.

## 2. Hangi Amaçla Kullanılır

`TokenPortService.RefreshAsync`, bir refresh token kullanıldığında (rotasyon) eskisini iptal edip yenisini oluşturur; `CreateAsync`/`FindByHashAsync`/`TryRevokeAsync` bu akışın veri katmanıdır.

## 3. Sorumlulukları

- Üstlendiği: token CRUD'u (yaratma, hash ile arama, iptal), rotasyonun ÇİFT KULLANIMA karşı atomik korunması.
- Üstlenmediği: token hash'inin üretimi (SHA-256, çağıran katmanın işi), token'ın kendisinin JWT olarak imzalanması (bkz. `JwtAccessTokenProvider`).

## 4. İlişkiler

- `IRefreshTokenRepository` portunu implemente eder.
- `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- `TokenPortService` (Application katmanı) tarafından çağrılır.

## 5. Tasarım Yaklaşımı

> 🐞 **`TryRevokeAsync` neden `RevokeAsync`'ten AYRI, koşullu bir metot olarak eklendi:** `RevokeAsync` klasik oku-değiştir-kaydet yapar (`FirstAsync` + alan ata + `SaveChangesAsync`) — iki eşzamanlı `/auth/refresh` isteği AYNI eski token'ı kullanmaya çalışırsa (örn. çift-tıklama, ağ retry'ı), her ikisi de token'ı "hâlâ geçerli" olarak okuyup ikisi de yeni bir refresh token üretebilir; bu, aynı eski token'ın İKİ KEZ "başarıyla" kullanılmasına (rotasyonun çift kullanım koruması delinmesine) yol açardı. `TryRevokeAsync`, tek bir `WHERE Id=@id AND RevokedAt IS NULL` koşullu `ExecuteUpdateAsync` ile bunu önler: yalnızca token GERÇEKTEN hâlâ aktifse (`RevokedAt == null`) iptal edilir ve `affected > 0` (yani `true`) döner; ikinci eşzamanlı çağrı `false` alır ve `TokenPortService.RefreshAsync` bu durumda yeni token ÜRETMEZ — projede approval kuyruğu ve stok düşümünde de kullanılan aynı "koşullu atomik sahiplenme" deseni (bkz. [PostgresApprovalQueue](../../Postgres/PostgresApprovalQueue.md), [StockDeduction](../../Postgres/StockDeduction.md)).

`RevokeAsync` (koşulsuz versiyon) hâlâ kodda duruyor ama `TokenPortService` artık onu kullanmıyor — logout da koşullu `TryRevokeAsync`'e geçti (koşulsuz yazma, araya giren bir rotasyonun `ReplacedByTokenHash` kaydını silip yeniden kullanım tespitini kör ediyordu).

`RevokeAllActiveForUserAsync`, refresh token yeniden kullanımı tespit edildiğinde kullanıcının tüm aktif token'larını `WHERE UserId = @id AND RevokedAt IS NULL` koşullu `ExecuteUpdateAsync` ile tek seferde iptal eder (bkz. [TokenPortService](../../../CustomerSupportBot.Application/Services/Auth/TokenPortService.md)).

> `ExecuteUpdateAsync` EF InMemory sağlayıcısında desteklenmez; bu metotları kullanan testler gerçek Postgres (Testcontainers) ya da `InMemoryRefreshTokenRepository` test ikizi kullanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task CreateAsync(string id, string userId, string tokenHash, DateTime expiresAt, DateTime createdAt, CancellationToken ct)` | Yeni refresh token kaydı ekler. |
| `Task<RefreshTokenInfo?> FindByHashAsync(string tokenHash, CancellationToken ct)` | Hash'e göre arar (token'ın kendisi DEĞİL, hash'i saklanır/aranır — DB sızıntısına karşı). |
| `Task RevokeAsync(string id, DateTime revokedAt, string? replacedByTokenHash, CancellationToken ct)` | Koşulsuz iptal — oku-değiştir-kaydet. |
| `Task<bool> TryRevokeAsync(string id, DateTime revokedAt, string? replacedByTokenHash, CancellationToken ct)` | Koşullu atomik iptal (`RevokedAt IS NULL` iken); rotasyonun çift-kullanım koruması budur. |
| `Task<int> RevokeAllActiveForUserAsync(string userId, DateTime revokedAt, CancellationToken ct)` | Kullanıcının aktif tüm token'larını koşullu toplu iptal; iptal edilen sayı. |
| `private static RefreshTokenInfo Map(RefreshTokenEntity e)` | Entity → Domain modeli. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`

## Bağlantılar

- [IRefreshTokenRepository](../../../CustomerSupportBot.Application/Ports/Outbound/Auth/IRefreshTokenRepository.md)
- [CustomerSupportDbContext](../CustomerSupportDbContext.md)
- [PostgresApprovalQueue](../../Postgres/PostgresApprovalQueue.md) — aynı koşullu-atomik desen
