// Adapters.Persistence/ExceptionTranslator.cs
// Infrastructure exception'larını domain exception'larına çevirir.
// Adapter katmanında kullanılır; Application katmanı saf domain exception'ları yakalar.

using CustomerSupportBot.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CustomerSupportBot.Adapters.Persistence;

/// <summary>
/// EF Core / Npgsql exception'larını domain exception'larına çevirir.
/// </summary>
internal static class ExceptionTranslator
{
    /// <summary>
    /// Infrastructure exception'ını uygun domain exception'ına çevirir.
    /// Tanınmayan exception'lar <see cref="PersistenceException"/> olarak wrap edilir.
    /// </summary>
    public static DomainException Translate(Exception ex, string? context = null)
    {
        return ex switch
        {
            DbUpdateConcurrencyException concurrency =>
                new ConcurrencyConflictException(
                    context ?? "Eşzamanlılık çakışması oluştu.", concurrency),

            DbUpdateException { InnerException: PostgresException pgEx } =>
                TranslatePostgres(pgEx, context),

            PostgresException pgEx =>
                TranslatePostgres(pgEx, context),

            InvalidOperationException { Message: var msg } when msg.Contains("connection") =>
                new PersistenceException(
                    context ?? "Veritabanı bağlantı hatası.", ex),

            TimeoutException =>
                new PersistenceException(
                    context ?? "Veritabanı işlemi zaman aşımına uğradı.", ex),

            _ => new PersistenceException(
                    context ?? "Veritabanı işlemi başarısız oldu.", ex)
        };
    }

    private static DomainException TranslatePostgres(PostgresException pgEx, string? context)
    {
        // https://www.postgresql.org/docs/current/errcodes-appendix.html
        return pgEx.SqlState switch
        {
            "23505" => // unique_violation
                new ConcurrencyConflictException(
                    context ?? "Kayıt zaten mevcut (unique constraint ihlali).", pgEx),

            "23503" => // foreign_key_violation
                new EntityNotFoundException("İlişkili kayıt", pgEx.Detail ?? "bilinmiyor"),

            "40001" or "40P01" => // serialization_failure / deadlock_detected
                new ConcurrencyConflictException(
                    context ?? "Deadlock veya serialization hatası.", pgEx),

            "57014" => // query_canceled (timeout)
                new PersistenceException(
                    context ?? "Sorgu zaman aşımına uğradı.", pgEx),

            "08000" or "08001" or "08003" or "08006" => // connection errors
                new PersistenceException(
                    context ?? "Veritabanı bağlantı hatası.", pgEx),

            _ => new PersistenceException(
                    context ?? $"PostgreSQL hatası: {pgEx.MessageText}", pgEx)
        };
    }
}
