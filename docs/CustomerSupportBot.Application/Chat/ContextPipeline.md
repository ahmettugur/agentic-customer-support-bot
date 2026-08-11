# ContextPipeline

**Dosya:** `Services/Chat/ContextPipeline.cs`

## 1. Ne İşe Yarar

Tüm IContextProvider implementasyonlarını sırayla çalıştırarak bağlam parçalarını toplar ve birleştirir. Pipeline pattern ile çalışır.

## 2. Hangi Amaçla Kullanılır

Her mesaj işlendiğinde reasoning ve agent prompt'larına ek bağlam sağlar.

## Bağlantılar

- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — Provider'lar
- [ChatPortService.md](ChatPortService.md) — Pipeline'ı çağıran orkestratör
