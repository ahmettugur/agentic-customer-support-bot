# NoopContextProvider

- **Kaynak:** `CustomerSupportBot.Application/Services/Providers/NoopContextProvider.cs`
- **Tür:** `public sealed class : IContextProvider`
- **Namespace:** `CustomerSupportBot.Application.Services.Providers`

## Ne işe yarar?

`NoopContextProvider`, [IContextProvider](IContextProvider.md) arayüzünün hiçbir işlem yapmayan (`Task.FromResult<string?>(null)`) boş (No-Operation) implementasyonudur.

## Hangi amaçla kullanılır`?

- Test senaryolarında veya dinamik bağlam sağlayıcıların devre dışı bırakıldığı durumlarda null referans hatalarını önlemek (Null Object Pattern).

## Metotlar ve İç Çalışma Mantıkları

### 1. `GetContextAsync`
```csharp
public Task<string?> GetContextAsync(
    AgentSession session,
    string currentQuery,
    CancellationToken ct = default)
```
- **Ne işe yarar?:** Daima `Task.FromResult<string?>(null)` döner.

## Bağımlılıklar

- [IContextProvider](IContextProvider.md)
