# ExceptionTranslator

**Dosya:** `ExceptionTranslator.cs`  
**Tür:** `public static class`

## Ne yapar?

EF Core ve Npgsql'den gelen altyapı exception'larını domain exception'larına çevirir. Postgres adaptörlerinin `catch` bloklarında çağrılır; Application katmanı altyapı detaylarından izole kalır.

---

## `Translate`

```csharp
public static Exception Translate(Exception ex)
```

Gelen exception'ı inceler ve uygun domain exception döner. Tanınmayan exception'larda orijinali sarmalar.

---

## Eşleme tablosu

| Gelen Exception | Koşul | Domain Exception |
|----------------|-------|-----------------|
| `DbUpdateConcurrencyException` | — | `ConcurrencyConflictException` |
| `PostgresException` | `SqlState = "23505"` (unique violation) | `DuplicateEntityException` |
| `PostgresException` | `SqlState = "23503"` (foreign key violation) | `EntityNotFoundException` |
| `PostgresException` | `SqlState = "40P01"` (deadlock) | `PersistenceException("Deadlock")` |
| `PostgresException` | `SqlState = "57014"` (query timeout) | `PersistenceException("Timeout")` |
| `PostgresException` | `SqlState = "08*"` (connection error) | `PersistenceException("Connection")` |
| `OperationCanceledException` | — | Yeniden fırlatılır (catch etme) |
| Diğer | — | `PersistenceException(orijinal)` |

---

## Kullanım

```csharp
try
{
    await db.SaveChangesAsync(ct);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    throw ExceptionTranslator.Translate(ex);
}
```

---

## Domain exception'lar

| Sınıf | Açıklama |
|-------|---------|
| `ConcurrencyConflictException` | Eş zamanlı güncelleme çakışması |
| `DuplicateEntityException` | Unique kısıtı ihlali |
| `EntityNotFoundException` | FK referans ettiği kayıt yok |
| `PersistenceException` | Genel altyapı hatası (deadlock, timeout, bağlantı) |

Bu exception'lar Application katmanında `try/catch` ile yakalanabilir; HTTP response'a dönüştürme API katmanında yapılır.
