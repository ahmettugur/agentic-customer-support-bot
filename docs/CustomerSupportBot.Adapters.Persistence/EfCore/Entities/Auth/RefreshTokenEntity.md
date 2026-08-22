# RefreshTokenEntity

**Dosya:** `EfCore/Entities/Auth/RefreshTokenEntity.cs`
**Şema/Tablo:** `auth.refresh_tokens`
**Configuration:** [RefreshTokenConfiguration](../../Configurations/Auth/RefreshTokenConfiguration.md)

## 1. Ne İşe Yarar

Bir kullanıcı oturumu için üretilmiş, **hash'lenmiş** opaque refresh token kaydını temsil eder;
`auth.refresh_tokens` tablosunun satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

`EfRefreshTokenRepository` (`EfCore/Auth/`) — access token süresi dolduğunda `/auth/refresh`
uç noktası bu tabloyu sorgulayıp yeni bir access token üretir; aynı anda eski token **rotate**
edilir (yenisiyle değiştirilir).

## 3. Sorumlulukları

- **Üstlendiği:** Token'ın hash'ini, sahibini, geçerlilik/iptal durumunu taşımak.
- **Üstlenmediği:** Token'ın kendisini saklamak — `TokenHash` alanı ham token'ın hash'idir, ham
  değer hiçbir yerde saklanmaz (çalınma riskine karşı).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`UserId` üzerinden [UserEntity](UserEntity.md)'ye **gerçek** foreign key ile bağlıdır
(`fk_refresh_tokens_user`, `Cascade` silme — kullanıcı silinirse tüm refresh token'ları da
silinir).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`RevokedAt`/`ReplacedByTokenHash` çifti, token **rotasyonu**nu modellemek içindir: bir token
kullanıldığında iptal edilmez-silinir değil, `RevokedAt` doldurulur ve `ReplacedByTokenHash`
onu değiştiren yeni token'ın hash'ine işaret eder — böylece "bu token daha önce kullanıldı mı,
kullanıldıysa hangisiyle değiştirildi" sorgulanabilir (token çalınma/tekrar-kullanım tespiti
için önemli bir denetim izi).

> 🐞 Rotasyon, tek bir koşullu `ExecuteUpdateAsync` (`WHERE Id = ... AND RevokedAt == null`)
> ile **atomik** yapılır — iki eşzamanlı `/auth/refresh` isteği aynı token'ı aynı anda rotate
> etmeye çalışırsa sadece biri kazanır, diğeri `null` döner. Bkz. `TokenPortService.RefreshAsync`.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `string` | Birincil anahtar. |
| `UserId` | `string` | FK → `UserEntity.Id`, index'li. |
| `TokenHash` | `string` | Token'ın hash'i, **unique** (`ux_refresh_tokens_hash`). |
| `ExpiresAt` | `DateTime` | Geçerlilik bitiş zamanı. |
| `CreatedAt` | `DateTime` | Üretilme zamanı. |
| `RevokedAt` | `DateTime?` | İptal/rotasyon zamanı (varsa). |
| `ReplacedByTokenHash` | `string?` | Rotasyonda yerini alan yeni token'ın hash'i. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [RefreshTokenConfiguration](../../Configurations/Auth/RefreshTokenConfiguration.md)
- [UserEntity](UserEntity.md)
- [EfRefreshTokenRepository](../../Auth/EfRefreshTokenRepository.md)
- [README](../README.md)
