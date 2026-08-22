# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.AI/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.AI`

## Ne işe yarar?

`ExceptionTranslator`, OpenAI SDK, Azure OpenAI SDK, HTTP ağ katmanı ve Qdrant gRPC kütüphanesinden fırlatılan altyapı istisnalarını (`ClientResultException`, `RpcException`, `HttpRequestException`, `TimeoutException`) Domain katmanının anlayacağı [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md), [EntityNotFoundException](../CustomerSupportBot.Domain/Exceptions/EntityNotFoundException.md) ve [ConcurrencyConflictException](../CustomerSupportBot.Domain/Exceptions/ConcurrencyConflictException.md) türlerine dönüştüren yardımcı sınıftır.

## Hangi amaçla kullanılır`?

Üst katmanların altyapı bağımlılıklarından (Azure SDK, gRPC durum kodları vb.) izole kalmasını sağlamak; Rate Limit (429), Kimlik Doğrulama (401/403), Sunucu Hataları (500) ve Qdrant durum kodlarını standart domain istisnalarına çevirmek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - `Translate` metodu ile HTTP ve OpenAI/Azure istisnalarını eşleştirmek.
  - `TranslateGrpc` metodu ile Qdrant gRPC hata kodlarını (`Unavailable`, `DeadlineExceeded`, `NotFound`, `AlreadyExists`) analiz etmek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Translate`
```csharp
public static DomainException Translate(Exception ex, string? context = null)
```
- **Ne işe yarar?:** Genel AI sağlayıcı istisnalarını domain istisnasına çevirir.
- **İç Mantığı:**
  - `ClientResultException { Status: 429 }` ➔ `ExternalServiceException` (Rate limit mesajıyla).
  - `ClientResultException { Status: 401 or 403 }` ➔ `ExternalServiceException` (Yetkilendirme hatası).
  - `ClientResultException { Status: >= 500 }` ➔ `ExternalServiceException` (Geçici servis arızası).
  - `ClientResultException { Status: 408 }` ➔ `ExternalServiceException` (İstek zaman aşımı).
  - `HttpRequestException` ➔ `ExternalServiceException` (Bağlantı kurulamadı).
  - `TaskCanceledException { InnerException: TimeoutException }` ➔ `ExternalServiceException` (Zaman aşımı).
  - `OperationCanceledException` ➔ `ExternalServiceException` (İstek iptal edildi).
  - `Grpc.Core.RpcException rpc` ➔ `TranslateGrpc(rpc, context)`.
  - Diğer ➔ `ExternalServiceException`.

### 2. `TranslateGrpc` (Private Static)
```csharp
private static DomainException TranslateGrpc(Grpc.Core.RpcException rpc, string? context)
```
- **Ne işe yarar?:** Qdrant gRPC istisnalarını domain istisnasına çevirir.
- **İç Mantığı:**
  - `StatusCode.Unavailable` ➔ `ExternalServiceException` ("Qdrant erişilemez").
  - `StatusCode.DeadlineExceeded` ➔ `ExternalServiceException` ("Qdrant zaman aşımı").
  - `StatusCode.NotFound` ➔ `EntityNotFoundException("VectorCollection", ...)`.
  - `StatusCode.AlreadyExists` ➔ `ConcurrencyConflictException` ("Qdrant kaynağı zaten mevcut").

## Bağımlılıklar

- [DomainException](../CustomerSupportBot.Domain/Exceptions/DomainException.md)
- [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md)
- [EntityNotFoundException](../CustomerSupportBot.Domain/Exceptions/EntityNotFoundException.md)
- [ConcurrencyConflictException](../CustomerSupportBot.Domain/Exceptions/ConcurrencyConflictException.md)
- `System.ClientModel.ClientResultException`
- `Grpc.Core.RpcException`
