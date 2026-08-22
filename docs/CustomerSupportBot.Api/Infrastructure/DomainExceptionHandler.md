# DomainExceptionHandler

- **Kaynak:** `CustomerSupportBot.Api/Infrastructure/DomainExceptionHandler.cs`
- **Tür:** `public sealed class : IExceptionHandler`
- **Namespace:** `CustomerSupportBot.Api.Infrastructure`

## Ne işe yarar?

`DomainExceptionHandler`, ASP.NET Core `IExceptionHandler` arayüzünü uygulayan; API uç noktalarında fırlatılan [DomainException](../../CustomerSupportBot.Domain/Exceptions/DomainException.md) türevlerini yakalayarak RFC 7807 uyumlu ProblemDetails JSON formatına ve uygun HTTP durum kodlarına (404, 400, 409, 502) dönüştüren global hata yakalama servisidir.

## Hangi amaçla kullanılır`?

- Uygulama içinde fırlatılan domain hatalarının istemciye ham C# istisnası veya 500 çökmesi olarak yansımasını engellemek.
- İstisna türüne göre HTTP yanıt kodunu ve hata detayını belirlemek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `TryHandleAsync`
```csharp
public async ValueTask<bool> TryHandleAsync(
    HttpContext httpContext,
    Exception exception,
    CancellationToken cancellationToken)
```
- **Ne işe yarar?:** Fırlatılan hatayı inceler ve HTTP yanıtını yazar.
- **İç Mantığı:**
  - `EntityNotFoundException` ➔ `Status = 404 (Not Found)`
  - `ValidationException` ➔ `Status = 400 (Bad Request)`
  - `ConcurrencyConflictException` ➔ `Status = 409 (Conflict)`
  - `UnauthorizedDomainException` ➔ `Status = 401 (Unauthorized)`
  - `ExternalServiceException` ➔ `Status = 502 (Bad Gateway)`
  - Diğer ➔ `Status = 500 (Internal Server Error)`
  - `httpContext.Response.WriteAsJsonAsync(problemDetails)` yazılarak `true` döndürülür.

## Bağımlılıklar

- `Microsoft.AspNetCore.Diagnostics.IExceptionHandler`
- [DomainException](../../CustomerSupportBot.Domain/Exceptions/DomainException.md)
