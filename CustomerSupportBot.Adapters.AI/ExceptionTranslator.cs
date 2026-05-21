// Adapters.AI/ExceptionTranslator.cs
// AI provider infrastructure exception'larını domain exception'larına çevirir.
// OpenAI, Azure OpenAI, Anthropic ve Qdrant hatalarını tutarlı domain exception'larına dönüştürür.

using System.ClientModel;
using CustomerSupportBot.Domain.Exceptions;

namespace CustomerSupportBot.Adapters.AI;

/// <summary>
/// AI provider exception'larını domain exception'larına çevirir.
/// </summary>
internal static class ExceptionTranslator
{
    public static DomainException Translate(Exception ex, string? context = null)
    {
        return ex switch
        {
            ClientResultException { Status: 429 } =>
                new ExternalServiceException("AI",
                    context ?? "AI servisine çok fazla istek gönderildi (rate limit).", ex),

            ClientResultException { Status: 401 or 403 } =>
                new ExternalServiceException("AI",
                    context ?? "AI servisine yetkilendirme başarısız.", ex),

            ClientResultException { Status: >= 500 } =>
                new ExternalServiceException("AI",
                    context ?? "AI servisi geçici olarak kullanılamıyor.", ex),

            ClientResultException { Status: 408 } =>
                new ExternalServiceException("AI",
                    context ?? "AI servis isteği zaman aşımına uğradı.", ex),

            HttpRequestException =>
                new ExternalServiceException("AI",
                    context ?? "AI servisine bağlantı kurulamadı.", ex),

            TaskCanceledException { InnerException: TimeoutException } =>
                new ExternalServiceException("AI",
                    context ?? "AI servis isteği zaman aşımına uğradı.", ex),

            OperationCanceledException =>
                new ExternalServiceException("AI",
                    context ?? "AI servis isteği iptal edildi.", ex),

            // Qdrant gRPC hataları
            Grpc.Core.RpcException rpc =>
                TranslateGrpc(rpc, context),

            _ => new ExternalServiceException("AI",
                    context ?? "AI servisi hatası oluştu.", ex)
        };
    }

    private static DomainException TranslateGrpc(Grpc.Core.RpcException rpc, string? context)
    {
        return rpc.StatusCode switch
        {
            Grpc.Core.StatusCode.Unavailable =>
                new ExternalServiceException("Qdrant",
                    context ?? "Qdrant vektör veritabanı erişilemez.", rpc),

            Grpc.Core.StatusCode.DeadlineExceeded =>
                new ExternalServiceException("Qdrant",
                    context ?? "Qdrant işlemi zaman aşımına uğradı.", rpc),

            Grpc.Core.StatusCode.NotFound =>
                new EntityNotFoundException("VectorCollection", rpc.Status.Detail ?? "bilinmiyor"),

            Grpc.Core.StatusCode.AlreadyExists =>
                new ConcurrencyConflictException(
                    context ?? "Qdrant kaynağı zaten mevcut.", rpc),

            _ => new ExternalServiceException("Qdrant",
                    context ?? $"Qdrant hatası: {rpc.Status.Detail}", rpc)
        };
    }
}
