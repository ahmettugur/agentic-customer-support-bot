# ExceptionTranslator

**Dosya:** `ExceptionTranslator.cs`  
**Tür:** `internal static class`

## Ne yapar?

EF Core ve Npgsql'den gelen altyapı exception'larını domain exception'larına çevirir. Postgres adaptörlerinin `catch` bloklarında çağrılır; Application katmanı altyapı detaylarından izole kalır.

---

## `Translate`

```csharp
public static DomainException Translate(Exception ex, string? context = null)
```

Gelen exception'ı inceler ve uygun `DomainException` alt sınıfını döner (üst tip `Exception` değil). `context` verilirse hata mesajının başına eklenir. Tanınmayan exception'lar `PersistenceException` olarak sarmalanır.

---

## Eşleme tablosu

| Gelen Exception | Koşul | Domain Exception |
|----------------|-------|-----------------|
| `DbUpdateConcurrencyException` | — | `ConcurrencyConflictException` |
| `DbUpdateException` içinde `PostgresException` | — | `TranslatePostgres` ile aşağıdaki tabloya göre |
| `PostgresException` | `SqlState = "23505"` (unique violation) | `ConcurrencyConflictException` (**`DuplicateEntityException` diye bir sınıf yoktur**) |
| `PostgresException` | `SqlState = "23503"` (foreign key violation) | `EntityNotFoundException("İlişkili kayıt", pgEx.Detail)` |
| `PostgresException` | `SqlState = "40001"` (serialization) veya `"40P01"` (deadlock) | `ConcurrencyConflictException` (`PersistenceException` **değil**) |
| `PostgresException` | `SqlState = "57014"` (query timeout) | `PersistenceException` |
| `PostgresException` | `SqlState = "08000/08001/08003/08006"` (connection error) | `PersistenceException` |
| `InvalidOperationException` (mesajında "connection" geçiyorsa) | — | `PersistenceException` |
| `TimeoutException` | — | `PersistenceException` |
| Diğer | — | `PersistenceException(orijinal)` |

`Translate` metodu **`OperationCanceledException`'ı özel olarak ele almaz** — yeniden fırlatma davranışı, çağıran tarafın `catch (Exception ex) when (ex is not OperationCanceledException)` filtresinden gelir (aşağıdaki kullanım örneğine bakın), `ExceptionTranslator` içinde değil.

---

## Kullanım

```csharp
try
{
    await db.SaveChangesAsync(ct);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    throw ExceptionTranslator.Translate(ex, "Sipariş kaydedilemedi.");
}
```

---

## Domain exception'lar

| Sınıf | Açıklama |
|-------|---------|
| `ConcurrencyConflictException` | Eş zamanlı güncelleme çakışması **veya** unique/deadlock/serialization ihlali |
| `EntityNotFoundException` | FK referans ettiği kayıt yok |
| `PersistenceException` | Genel altyapı hatası (timeout, bağlantı, tanınmayan hata) |

Bu exception'lar Application katmanında `try/catch` ile yakalanabilir; HTTP response'a dönüştürme API katmanında yapılır.
