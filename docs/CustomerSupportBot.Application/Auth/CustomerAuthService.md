# CustomerAuthService

**Dosya:** `Services/Auth/CustomerAuthService.cs`

## 1. Ne İşe Yarar

Müşteri kimlik doğrulama (login) işlemlerini yönetir — müşteri numarası ile giriş, JWT token üretimi ve `SessionState.AuthenticatedCustomerId` set etme.

## 2. Hangi Amaçla Kullanılır

Kullanıcı chat içinden "giriş yap" dediğinde veya login endpoint'i çağrıldığında bu servis çalışır. Başarılı login sonrası session'a güvenilir müşteri kimliği yazılır.

> 💡 **Analiz notu:** Banka şubesinde kimlik doğrulama gibi — TC kimlik göster, sistem doğrula, güvenli işlemlere izin ver.

## Bağlantılar

- [TokenPortService.md](TokenPortService.md) — JWT token üretimi
- [UserService.md](UserService.md) — Admin kullanıcı kimlik doğrulama
