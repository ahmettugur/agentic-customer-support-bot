# SemanticMemoryContextProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/SemanticMemoryContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`SemanticMemoryContextProvider`, kullanıcının anlık mesajını ([SemanticMemoryService](../Memory/SemanticMemoryService.md)) üzerinden Qdrant vektör ambarında aratarak, şirketin iade, kargo, garanti ve SSS kurallarını içeren ilgili RAG makale parçacıklarını `"## 📚 İlgili Bilgi Bankası (RAG)"` bloğu halinde bağlama ekleyen sağlayıcıdır.

## Hangi amaçla kullanılır`?

- Ajanların şirket politikaları hakkında doğru ve güncel bilgilerle yanıt vermesini sağlamak (Hallucination önleme).
- [ContextSanitizer](../Memory/ContextSanitizer.md) ile vektör ambarından gelen harici metinleri temizleyerek prompt enjeksiyonu riskini bertaraf etmek.

## Constructor ve Başlatma Mantığı

```csharp
public SemanticMemoryContextProvider(
    IMemoryPort memoryPort,
    IContextSanitizer sanitizer,
    ILogger<SemanticMemoryContextProvider> logger)
```

### Constructor İçerisinde Yapılan İşler:
- `_memoryPort`: Vektör anlamsal arama portu (`IMemoryPort`).
- `_sanitizer`: Metin sterilizasyon arayüzü (`IContextSanitizer`).
- `_logger`: Günlükleme motoru.

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetContextAsync`
```csharp
public async Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Kullanıcı sorgusuna en yakın 3 bilgi bankası dokümanını çeker ve formatlar.
- **İç Mantığı:**
  1. `_memoryPort.SearchAsync(currentQuery, limit: 3, ct)` ile anlamsal arama yapılır.
  2. Sonuç yoksa `null` döner.
  3. Dönen dokümanların başlık ve içerikleri `_sanitizer.Sanitize` ile temizlenir ve numaralandırılmış liste olarak birleştirilir.

## Özellikler/Properties

- `Name` (`string`): Sabit `"SemanticMemory"`.
- `Order` (`int`): `7`.

## Bağımlılıklar

- [IContextProvider](IContextProvider.md)
- [IMemoryPort](../Memory/MemoryPortService.md)
- [IContextSanitizer](../Memory/ContextSanitizer.md)
