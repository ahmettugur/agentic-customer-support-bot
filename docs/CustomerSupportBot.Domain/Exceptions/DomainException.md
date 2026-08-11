# Domain Exceptions

**Dosya:** `Exceptions/DomainException.cs`

Tek dosyada base sınıf + 4 alt sınıf. Application katmanı bunları `catch` eder; API katmanı HTTP status code'a çevirir.

---

## DomainException (base)

```csharp
public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message, Exception? innerException = null);
}
```

`abstract` değildir — doğrudan da örneklenebilir, ama pratikte her zaman alt sınıflarından biri fırlatılır. `Code` her alt sınıfta ilgili ctor içinde **sabit** olarak set edilir (çağıran taraf `code` parametresi geçmez — sadece `DomainException`'ın kendisi 2 parametre alır).

**Code:** Makine-okunabilir hata kodu. API katmanı bunu response body'e koyar; client lokalize mesaj seçebilir.

---

## Alt sınıflar

### EntityNotFoundException

Resource bulunamadı (session, order, customer, complaint, vb.).

```csharp
public EntityNotFoundException(string entityType, string entityId)
```

`Code` sabittir: `"ENTITY_NOT_FOUND"`. Mesaj otomatik üretilir: `"{entityType} bulunamadı: '{entityId}'"`. `EntityType`/`EntityId` property'leri de saklanır.

| Özellik | Değer |
|---|---|
| HTTP karşılığı | `404 Not Found` |
| Tipik kaynak | DB lookup, cache miss |
| Retry edilir mi? | **Hayır** — resource yoksa tekrar denemek anlamsız |

### PersistenceException

Veritabanı katmanından gelen **geçici** hatalar (timeout, deadlock, connection loss).

```csharp
public PersistenceException(string message, Exception? innerException = null)
```

`Code` sabittir: `"PERSISTENCE_ERROR"`.

| Özellik | Değer |
|---|---|
| HTTP karşılığı | `503 Service Unavailable` |
| Tipik kaynak | `ExceptionTranslator` (40P01 deadlock, 57014 timeout, 08* bağlantı) |
| Retry edilir mi? | **Evet** — exponential backoff ile |

### ExternalServiceException

3rd-party servis erişilemiyor (AI provider, Redis, dış API).

```csharp
public ExternalServiceException(string serviceName, string message, Exception? innerException = null)
```

`Code` sabittir: `"EXTERNAL_SERVICE_ERROR"`. `ServiceName` property'si ayrıca saklanır.

| Özellik | Değer |
|---|---|
| HTTP karşılığı | `502 Bad Gateway` |
| Tipik kaynak | OpenAI 429/500, Redis bağlantı, Qdrant timeout |
| Retry edilir mi? | **Bazen** — circuit breaker pattern uygun |

### ConcurrencyConflictException

Eş zamanlı güncelleme çakışması (iki admin aynı approval'ı aynı anda kararlaştırır).

```csharp
public ConcurrencyConflictException(string message, Exception? innerException = null)
```

`Code` sabittir: `"CONCURRENCY_CONFLICT"`.

| Özellik | Değer |
|---|---|
| HTTP karşılığı | `409 Conflict` |
| Tipik kaynak | `DbUpdateConcurrencyException` (EF Core optimistic concurrency) |
| Retry edilir mi? | **Hayır** — kullanıcıya "başkası karar verdi" göster |

---

## Kullanım örneği

```csharp
try
{
    var order = await orderRepo.GetAsync(orderId, ct);
    if (order is null)
        throw new EntityNotFoundException("Order", orderId);
    // ...
}
catch (PersistenceException ex)
{
    logger.LogWarning(ex, "DB transient error, retry suggested");
    throw;  // API katmanına yükselt
}
```

---

## Neden DomainException?

`Exception` direkt kullanmak yerine bu hiyerarşi:
- API katmanı tek `catch (DomainException ex)` ile tümünü yakalar
- `Code` property'si client-side lokalizasyon için yeterli
- Stack trace + inner exception alt yapı detaylarını saklar
