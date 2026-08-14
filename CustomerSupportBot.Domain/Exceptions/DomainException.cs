namespace CustomerSupportBot.Domain.Exceptions;

/// <summary>
/// Tüm domain exception'larının temel sınıfı.
/// Infrastructure katmanından gelen hatalar bu hiyerarşiye çevrilmelidir.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}

/// <summary>
/// İstenen kaynak bulunamadı (session, order, customer vb.).
/// </summary>
public class EntityNotFoundException : DomainException
{
    public string EntityType { get; }
    public string EntityId { get; }

    public EntityNotFoundException(string entityType, string entityId)
        : base("ENTITY_NOT_FOUND", $"{entityType} bulunamadı: '{entityId}'")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

/// <summary>
/// Kalıcılık katmanında geçici bir hata oluştu (connection timeout, deadlock vb.).
/// Retry mekanizması bu exception'ı yakalayabilir.
/// </summary>
public class PersistenceException : DomainException
{
    public PersistenceException(string message, Exception? innerException = null)
        : base("PERSISTENCE_ERROR", message, innerException)
    {
    }
}

/// <summary>
/// Dış servis (AI, Redis vb.) geçici olarak erişilemez.
/// </summary>
public class ExternalServiceException : DomainException
{
    public string ServiceName { get; }

    public ExternalServiceException(string serviceName, string message, Exception? innerException = null)
        : base("EXTERNAL_SERVICE_ERROR", message, innerException)
    {
        ServiceName = serviceName;
    }
}

/// <summary>
/// Bir oturuma, o oturumun sahibi olmayan bir müşteri adına erişilmeye çalışıldı.
///
/// <para>
/// <c>sessionId</c> her zaman istemciden gelir (URL ya da gövde). Kimlik doğrulama "bu kişi bir
/// müşteri mi" sorusunu yanıtlar, "bu oturum onun mu" sorusunu değil — ikincisini
/// <c>SessionIdentityBinder</c> yanıtlar ve ihlalde bu exception fırlatılır.
/// </para>
/// </summary>
public class UnauthorizedSessionAccessException : DomainException
{
    public string SessionId { get; }

    public UnauthorizedSessionAccessException(string sessionId)
        : base("SESSION_FORBIDDEN", "Bu oturuma erişim yetkiniz yok.")
        => SessionId = sessionId;
}

/// <summary>
/// Eşzamanlılık çakışması (örn: aynı approval'ı iki admin aynı anda onaylıyor).
/// </summary>
public class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base("CONCURRENCY_CONFLICT", message, innerException)
    {
    }
}
