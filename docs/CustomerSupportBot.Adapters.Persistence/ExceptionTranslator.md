# ExceptionTranslator

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/ExceptionTranslator.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence`

## Ne işe yarar?

`ExceptionTranslator`, PostgreSQL (Npgsql) ve Entity Framework Core altyapı istisnalarını (`PostgresException`, `DbUpdateConcurrencyException`, `DbUpdateException`) Domain katmanındaki [ConcurrencyConflictException](../CustomerSupportBot.Domain/Exceptions/ConcurrencyConflictException.md), [EntityNotFoundException](../CustomerSupportBot.Domain/Exceptions/EntityNotFoundException.md) ve [ExternalServiceException](../CustomerSupportBot.Domain/Exceptions/ExternalServiceException.md) türlerine dönüştüren yardımcı sınıftır.

## Hangi amaçla kullanılır`?

- Benzersizlik ihlali (Unique Constraint `23505`) ve yabancı anahtar ihlali (`23503`) gibi PostgreSQL SQLState kodlarını standart domain istisnalarına çevirmek.
- İyimser kilitlenme (Optimistic Concurrency) hatalarını yakalamak.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Translate`
```csharp
public static DomainException Translate(Exception ex, string? context = null)
```
- **Ne işe yarar?:** Veritabanı istisnasını domain istisnasına dönüştürür.
- **İç Mantığı:**
  - `PostgresException { SqlState: "23505" }` ➔ `ConcurrencyConflictException` (Benzersizlik çakışması).
  - `PostgresException { SqlState: "23503" }` ➔ `EntityNotFoundException` (İlişkili kayıt bulunamadı).
  - `DbUpdateConcurrencyException` ➔ `ConcurrencyConflictException` (Eşzamanlı güncelleme çakışması).
  - Diğer ➔ `ExternalServiceException("Postgres", ...)`

## Bağımlılıklar

- [DomainException](../CustomerSupportBot.Domain/Exceptions/DomainException.md)
- [ConcurrencyConflictException](../CustomerSupportBot.Domain/Exceptions/ConcurrencyConflictException.md)
- [EntityNotFoundException](../CustomerSupportBot.Domain/Exceptions/EntityNotFoundException.md)
- `Npgsql.PostgresException`
